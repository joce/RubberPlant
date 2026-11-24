# Direct-from-`.rp` 3D viewer (BP.2)

Status: **spec** (Phase 2 of [`battle-plan.md`](battle-plan.md), targeting `v0.3` tooling): a direct `.rp` → GPU 3D viewer (`RubberPlant.Viewer`) with headless offscreen PNG/silhouette rendering, `batch` mode, and an interactive inspection window.

This document specifies the GPU stack, pure camera/scene/PNG core, rendering pipeline, unified command protocol, CLI and interactive UX, and CI verification strategy so both humans and automated agents can inspect `.rp` models in 3D without an OBJ/glTF round-trip.

---

## 1. Scope and architecture

| In BP.2 | Not in BP.2 |
|---|---|
| Separate `rubberplant-viewer` executable (`src/RubberPlant.Viewer`) | Embedding GPU/GLFW dependencies into `rubberplant` (`RubberPlant.Cli`) |
| Direct in-memory pipeline: `.rp` → `Pipeline` → `Stats` & `Mesh.build` → OpenGL 3.3 Core | Generalized cylinders or `.s` surfaces (Phase 3) |
| Headless offscreen PNGs: `snapshot`, 2×2 `contact-sheet`, binary `silhouette` | Textures, shadows, PBR materials, or custom shader authoring |
| Stateful `batch` mode (`exit <code>` framing) and one-shot CLI (`--stats` JSON) | File-watching auto-reload (`--watch`); `R` / `reload` cover BP.2 |
| Interactive window (orbit, pan, zoom, shortcuts) with live stdin command bridge | Orthographic camera toggle (can be added later for exact SVG/ABOP comparison) |
| Cross-platform CI with software OpenGL (Mesa) + Windows Native AOT build | Direct EGL surfaceless Linux GPU context without Xvfb (defer to Phase 4/5 if needed) |

Architecture preserves the repository's **pure core, thin I/O shell** boundary:

- **`RubberPlant.Core` and `RubberPlant.Backends` are unchanged** (no GPU dependencies, no new I/O).
- **`RubberPlant.Cli` stays GPU-free.** `RubberPlant.Viewer` is a separate executable referencing `RubberPlant.Core` and `RubberPlant.Backends`.
- **Inside `RubberPlant.Viewer`, all math, geometry packing, PNG encoding, and command parsing are pure F#** and unit-tested without an OpenGL context. Only `GlContext.fs`, `Shaders.fs`, `Renderer.fs`, and `Program.fs` touch GLFW, OpenGL, or the filesystem.
- **Repository build invariants apply to `src/RubberPlant.Viewer`:** `net10.0`, `TreatWarningsAsErrors`, `WarnOn 3390;1182`, `--reflectionfree`, and the string-only `%s` printf rule checked by `CliTests.aotFormatTests`.

```
.rp file + overrides + Limits
  ──► Pipeline.compile ──► Pipeline.runWith ──► Output
                                                  ├──► Stats.analyze ──► Stats.toJson (UTF-8 JSON)
                                                  └──► Mesh.build ──► Scene.fromMesh (VBO / IBO / Grid)
                                                                           │
                                                                           ▼
                                                                    OpenGL 3.3 Core
                                                                ┌──────────┴──────────┐
                                                                ▼                     ▼
                                                       Interactive Window      Offscreen FBOs
                                                      (orbit/pan/zoom +       (4x MSAA shaded /
                                                       live stdin queue)       1x binary mask)
                                                                                      │
                                                                                      ▼
                                                                             Png.encode (pure F#)
                                                                           ──► atomic PNG write
```

---

## 2. GPU stack and platform support

### 2.1 Primary binding: `Silk.NET.GLFW` + `Silk.NET.OpenGL` (OpenGL 3.3 Core)

`RubberPlant.Viewer` targets **OpenGL 3.3 Core** using **`Silk.NET.GLFW` + `Silk.NET.OpenGL` (2.23.0)** directly, explicitly **omitting** `Silk.NET.Windowing` and `Silk.NET.Input`:

- **32-bit indices are mandatory:** 10 of the 40 existing `examples/*.rp` models exceed 65,535 vertices at `--sides 12` (up to 314,904 vertices / 944,712 indices on `fig2-8b.rp`, `fig2-8c.rp`, and `fig2-8c-figure.rp` — 11.3 MB of GPU buffers). OpenGL 3.3 Core natively supports `GL_UNSIGNED_INT` index buffers.
- **Exact FBO control:** supports a 4× MSAA renderbuffer + resolve FBO for crisp shaded renders and a dedicated 1× non-MSAA FBO for strictly binary `{0, 255}` silhouettes.
- **Clean headless + interactive loop:** GLFW supports hidden contexts (`GLFW_VISIBLE = GLFW_FALSE`) for headless rendering and `glfwWaitEvents()` + thread-safe `glfwPostEmptyEvent()` for zero-CPU interactive idle with live stdin commands.
- **Zero image-encoding dependencies:** PNG output is encoded directly in pure F# using `System.IO.Compression.ZLibStream` and CRC-32 (§5.6).

#### Evaluated alternatives (rejected)

| Candidate | Reason for rejection |
|---|---|
| `Raylib-cs` | `Mesh` uses 16-bit `ushort*` indices (breaks 10/40 examples) and lacks native MSAA `RenderTexture`. |
| `Veldrid` | Unmaintained/archived; high Native AOT and trimming friction. |
| `OpenTK` | 4.x has trimming friction; 5.x remains preview. |
| `Silk.NET.WebGPU` | Excessive pipeline/render-pass boilerplate and ~20 MB `wgpu-native` binary for a static-mesh viewer. |

### 2.2 Native AOT gate and direct-P/Invoke fallback

`Silk.NET.Core`'s default native library loader uses `Assembly.Location` / `DependencyContext`, which triggers `IL3000` and `IL3002` under `<PublishAot>true</PublishAot>` (and with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` requires `<NoWarn>IL2104;IL3053;IL3000;IL3002</NoWarn>` plus verification that `glfw3.dll` loads from `AppContext.BaseDirectory`).

**Gate at the start of PR 2:** build and run a minimal Native AOT smoke binary (`Glfw.GetApi()` + `GL.GetApi` + hidden context creation + `glfw3.dll` next to the published `.exe`). If `Silk.NET.Core` introduces blocking AOT runtime failures or unacceptable publish overhead, fall back immediately to **direct P/Invoke over `Ultz.Native.GLFW`** (~25 GLFW functions + ~45 OpenGL 3.3 Core function pointers loaded via `glfwGetProcAddress`), which requires zero managed GL dependencies and produces zero AOT warnings.

### 2.3 Headless rendering across environments

- **Local Windows (RTX 4070 Laptop):** GLFW hidden window (`GLFW_VISIBLE = GLFW_FALSE`) creates a hardware-accelerated OpenGL 3.3 Core context with no visible window or taskbar entry.
- **`ubuntu-latest` CI:** run under `xvfb-run -a` with Mesa `llvmpipe`. *(Upstream GLFW 3.4's `GLFW_PLATFORM_NULL` only supports `GLFW_OSMESA_CONTEXT_API`, not `GLFW_EGL_CONTEXT_API`, so Xvfb + Mesa is the clean Linux headless baseline.)*
- **`windows-latest` CI:** install Mesa software `opengl32.dll` via `ssciwr/setup-mesa-dist-win@v3` so both `RubberPlant.Viewer.Tests` and the published Windows Native AOT `rubberplant-viewer.exe` execute real OpenGL renders on GitHub-hosted runners.

---

## 3. Coordinate system, camera, and framing (`Camera.fs`)

World coordinates match [`backends.md` §2](backends.md#2-world-coordinates-and-units): right-handed, **`+Y` up**, `+X` right, `+Z` toward a front viewer. All trigonometry uses `Builtins.sinCosDeg` and `Builtins.tanDeg` for cross-platform determinism.

### 3.1 Pole-free orbit camera

A camera is parameterized by `(target, azimuthDeg, elevationDeg, distance)` in **origin-centered local GPU coordinates**, where `(0, 0, 0)` is the loaded mesh's AABB center (§4.1):

- `target`: camera orbit/pan pivot in local GPU coordinates (`(0, 0, 0)` when centered/fitted).
- `azimuthDeg` (`az`): finite angle in degrees (`0°` = front `+Z`, `+90°` = right side `+X`, `180°` = back `-Z`).
- `elevationDeg` (`el`): finite angle in `[-90, 90]` (`0°` = level horizon, `+90°` = top-down `+Y`, `-90°` = bottom-up `-Y`). Values outside `[-90, 90]` are rejected by CLI/protocol parsing (exit code `2`) and clamped during mouse orbit.
- `distance` (`dist`): strictly positive finite number (`dist > 0`).

To avoid `lookAt` gimbal lock at `el = ±90°`, the camera's orthonormal right-handed basis is constructed analytically:

```text
dir   = ( sin(az) * cos(el),  sin(el),  cos(az) * cos(el) )   // target -> camera
right = ( cos(az),            0,       -sin(az)           )   // camera +X
up    = cross(dir, right)                                     // camera +Y
pos   = target + distance * dir
```

Verification at canonical orientations:
- `Front (0°, 0°)`: `dir = (0, 0, 1)`, `right = (1, 0, 0)`, `up = (0, 1, 0)`.
- `Side +90° (90°, 0°)`: `dir = (1, 0, 0)`, `right = (0, 0, -1)`, `up = (0, 1, 0)`.
- `Top-Down (0°, 90°)`: `dir = (0, 1, 0)`, `right = (1, 0, 0)`, `up = (0, 0, -1)`.

### 3.2 Aspect-aware exact perspective `fit`

- Vertical field of view: **`fovY = 35°`**, framing padding **`pad = 1.15`**.
- `fit` resets `target` to `(0, 0, 0)` (the mesh center). Given the centered mesh AABB's 8 corners $c_0 \dots c_7$, half-diagonal $R$, viewport aspect ratio $a = w / h$, and $t_y = \tan(\text{fovY}/2)$:
  1. Project each corner into camera space:
     $$x_i = c_i \cdot \text{right}, \quad y_i = c_i \cdot \text{up}, \quad z_i = c_i \cdot \text{dir}$$
  2. Solve for the tightest distance that keeps every corner inside the padded frustum:
     $$d = \max_{i=0..7} \left( z_i + \max\!\left( \frac{\text{pad} \cdot |y_i|}{t_y},\; \frac{\text{pad} \cdot |x_i|}{a \cdot t_y},\; \epsilon_{\text{clear}} \right) \right)$$
     where $\epsilon_{\text{clear}} = 10^{-3} \cdot R$ ensures a degenerate 1D stick viewed end-on ($x_i = y_i = 0$) never places the camera directly on top of the geometry while staying homogeneous of degree 1 across arbitrary model scales.
- **Empty / zero-extent fallback:** when `mesh.Groups` is empty or $R = 0$, `fit` uses a unit bounding box centered at `(0, 0, 0)` with $R = 1$.

### 3.3 Canonical views and shared-scale contact sheet

| Shortcut | Label | `(az, el)` | Contact-sheet quadrant |
|---|---|---|---|
| `1` | `FRONT` | `(0°, 0°)` | Top-Left |
| `2` | `SIDE +90` | `(90°, 0°)` | Top-Right |
| `3` | `3/4 ISO` | `(35°, 25°)` | Bottom-Left |
| `4` | `TOP` | `(0°, 90°)` | Bottom-Right |

- Default interactive camera on `load`: **`3/4 ISO` (`35°, 25°`)**, auto-fitted to the viewport.
- **Contact-sheet shared physical scale:** compute `fit` distance $d_k$ (at square aspect $a = 1$, centered `target = (0, 0, 0)`) for each of the four canonical views and render all four quadrants using $d_{\text{sheet}} = \max(d_1, d_2, d_3, d_4)$. This guarantees that the plant's height and crown width are directly visually comparable across Front, Side, Iso, and Top-Down tiles.

### 3.4 Scale-invariant, continuous clip planes

To prevent near-plane clipping of the ground grid on shallow wide models, keep the pivot target visible on extreme close-ups, and maintain 24-bit depth precision across huge models (e.g., `fig2-8c.rp` with $R \approx 18{,}740$) and panned views:

- Let `modelRadius` be the mesh AABB half-diagonal (`1.0` if empty or zero-extent), and `effectiveRadius = modelRadius + |target|`.
- Let `gridExtentIfVisible` be the maximum Euclidean distance from local `(0, 0, 0)` to the four corners of the rendered ground grid (accounting for `y_local = -center.Y`), or `0.0` when the grid is disabled or omitted.
- `maxExtent = max(effectiveRadius, gridExtentIfVisible + |target|)`
- `far = max(distance + 2.0 * maxExtent, distance * 2.0)`
- `safeNear = min(distance * 0.1, max(distance * 0.01, far / 10000.0))`
- With grid visible:
  `near = max(min(distance - 1.1 * effectiveRadius, distance * 0.05), safeNear)`
- Without grid:
  `near = max(distance - 1.1 * effectiveRadius, safeNear)`

This formula is continuous everywhere (zero jump when crossing the bounding sphere), guarantees `0 < near <= 0.1 * distance < far`, keeps `far / near <= 10,000` whenever `distance >= 0.001 * far`, and scales homogeneously of degree 1 with world units.

---

## 4. Scene preparation and ground grid (`Scene.fs`)

### 4.1 Origin-centered GPU buffers

`Scene.fromMesh (mesh: Mesh)` prepares pure CPU arrays ready for single-shot OpenGL upload:

- **Empty / degenerate-only guard:** keyed strictly on `mesh.Groups.IsEmpty` (independent of `Stats.BBox`, since zero-length/zero-width segments produce `Stats.BBox <> None` while `Mesh.build` returns `{ Groups = [] }`).
- **Overflow-safe double-precision centering:**
  - Compute mesh AABB `min` and `max` across all vertices in `mesh.Groups` in `float` (double precision).
  - Compute `center = (0.5 * min.X + 0.5 * max.X, 0.5 * min.Y + 0.5 * max.Y, 0.5 * min.Z + 0.5 * max.Z)` (overflow-safe, matching `Stats.fs`).
  - Verify that the centered half-extents fit in finite `float32`; if not, return `Error` (`exit 1`).
  - Subtract `center` in double precision before casting to `float32`, preventing single-precision jitter on meshes far from the origin.
- **Buffer layout:**
  - One interleaved `float32[]` vertex buffer: 6 floats per vertex (`px, py, pz, nx, ny, nz`).
  - One `uint32[]` index buffer (`MeshGroup.Triangles` shifted by each group's base vertex offset).
  - Up to 8 `DrawRange` records `{ Color: int; FirstIndex: int; IndexCount: int }` (one per non-empty `MeshGroup`, in ascending `Color` order).

### 4.2 Auto-scaled ground grid (`y = 0` plane)

When `grid` is enabled, `Scene.buildGrid` generates line vertices on the world `y = 0` plane (matching the turtle's starting elevation; models that dip below `y = 0` such as `dragon.rp`, `koch-island.rp`, and `fig3-19-lilac.rp` naturally intersect the plane):

- **Reference span:** `span = max(extent.X, extent.Z, extent.Y * 0.5)` (falling back to `1.0` only when `mesh.Groups` is empty or `span = 0.0`, preserving scale invariance on tiny models).
- **Deterministic nice step size:** pick the smallest `step` in `{1, 2, 5} × 10^k` such that `2.5 * span / step <= 16.0` (equivalently `step >= 2.5 * span / 16.0`), which uniquely yields 8–16 cells across `2.5 * span` with grid half-size `half = ceil(1.25 * span / step) * step`.
- **Snapping and local-frame shift:**
  - Center the grid at `(snap(center.X, step), 0.0, snap(center.Z, step))`.
  - Shift every grid vertex by `-center` in double precision (`y_local = float32 (-center.Y)`) so it renders in the exact same origin-centered GPU space as the plant.
  - If `|center.Y|` or any grid endpoint exceeds finite `float32`, omit the grid cleanly instead of producing non-finite vertices.
- **Visual hierarchy:** subtle cool-gray minor lines, slightly darker major lines every 5 steps, a muted red-tinted X axis along `z = 0` whenever `z = 0` lies within the grid's Z bounds, and a muted blue-tinted Z axis along `x = 0` whenever `x = 0` lies within the grid's X bounds.

---

## 5. Rendering, lighting, and output modes

### 5.1 Framebuffers and readback (`GlContext.fs`, `Renderer.fs`)

- **Resolution bounds:** width and height must be positive integers `<= 8192` (values `< 1` or `> 8192` are usage errors, exit code `2`), and at runtime must not exceed `GL_MAX_RENDERBUFFER_SIZE` (exceeding the driver's limit fails with a runtime error, exit code `1`).
- **Shaded offscreen FBO:** lazily allocated and resized when target `(w, h)` changes:
  - Multisample FBO: `GL_RGB8` color renderbuffer + `GL_DEPTH_COMPONENT24` depth renderbuffer at `min(4, GL_MAX_SAMPLES)` samples (gracefully falls back to `1` sample if a software driver reports `MAX_SAMPLES < 2`).
  - Resolve FBO: non-multisample `GL_RGB8` texture/renderbuffer populated via `glBlitFramebuffer(..., GL_COLOR_BUFFER_BIT, GL_NEAREST)`.
- **Silhouette offscreen FBO:** dedicated **1× non-MSAA** FBO (`GL_R8` or `GL_RGB8` color + `GL_DEPTH_COMPONENT24` depth) so every pixel is strictly `0` or `255` with zero anti-aliased edge gray.
- **Readback correctness:**
  - Always call `glPixelStorei(GL_PACK_ALIGNMENT, 1)` and `glReadBuffer(GL_COLOR_ATTACHMENT0)` before `glReadPixels`, preventing 4-byte row padding shear on odd widths (e.g., `--size 301`).
  - When an offscreen command runs during interactive window mode, restore `glBindFramebuffer(GL_FRAMEBUFFER, 0)` and the physical window `glViewport(0, 0, fbW, fbH)` before the next redraw.

### 5.2 Lighting and shaders (`Shaders.fs`)

Three GLSL 330 Core shader programs:

1. **Two-sided linear-space lit shader** (plant mesh):
   - Back-face culling is **disabled** (`glDisable(GL_CULL_FACE)`) so open cylinders and two-sided polygons (`backends.md` §3) render from both sides; the fragment shader flips the normal when `!gl_FrontFacing`.
   - Base albedo comes from `Palette.linear[color]` (`Palette.fs`), ensuring color parity with glTF/OBJ/SVG.
   - **Lighting rig** (tuned so Color 0 `#1a1a1a` — used by most ABOP Ch 1–2 trees — shows clear 3D cylindrical form from every angle, including back views):
     - Light neutral-gray background (`#f2f2f0` sRGB).
     - Hemisphere ambient (`~0.38`, slightly warmer sky `+Y` / cooler ground `-Y`).
     - Warm directional key light from upper front-right (world direction `normalize(0.55, 0.80, 0.45)`, intensity `~0.85`).
     - Cool directional fill light from opposite back-left (`normalize(-0.60, 0.35, -0.55)`, intensity `~0.35`) so back sides (`az = 180°`) never crush to flat black.
     - Subtle Blinn-Phong view specular (strength `~0.06`, shininess `32`) to give dark trunks crisp cylindrical highlights.
   - **Exact IEC linear → sRGB transfer** in the fragment shader (clamped to `[0, 1]`):
     `c <= 0.0031308 ? 12.92 * c : 1.055 * pow(c, 1.0 / 2.4) - 0.055`.
2. **Flat unlit uniform-color shader:**
   - Used for `silhouette` (`vec3(1.0)` on black `vec3(0.0)` background) and for the wireframe overlay pass (dark gray `#303030`).
3. **Unlit vertex-colored line shader:**
   - Used for the ground grid (`GL_LINES`).

### 5.3 Wireframe overlay

When `wireframe` is enabled on shaded renders (window, `snapshot`, `contact-sheet`):

1. Enable `GL_POLYGON_OFFSET_FILL` with `glPolygonOffset(1.0f, 1.0f)` during the **shaded fill pass**, then disable `GL_POLYGON_OFFSET_FILL`.
2. Switch to `glPolygonMode(GL_FRONT_AND_BACK, GL_LINE)` and draw the mesh with the **flat unlit dark-gray shader**.
3. Restore `glPolygonMode(GL_FRONT_AND_BACK, GL_FILL)`.

*(Offsetting the fill pass rather than the line pass avoids driver-dependent `GL_POLYGON_OFFSET_LINE` quirks and ensures uniform dark wireframes without `gl_FrontFacing` artifacts on line primitives.)*

### 5.4 Silhouette mode

- Renders into the **1× non-MSAA** silhouette FBO with clear color `(0, 0, 0)` and flat white `(1, 1, 1)` mesh geometry.
- **Ignores `grid` and `wireframe`** in stateful (`batch` / interactive) sessions so switching between inspection and mask capture requires no extra toggles.
- Encodes an **8-bit grayscale PNG** (`ColorType = 0`, 1 byte/pixel) whose pixels are strictly `0uy` (background) or `255uy` (foreground).

### 5.5 2×2 contact sheet layout

- Output is a square RGB24 PNG of `--size <px>` × `<px>` (must be an **even integer ≥ 128**, capped at `8192`; default **`1024`**, giving four `512×512` tiles).
- Renders the four canonical views (§3.3) at shared scale $d_{\text{sheet}}$ into the shaded MSAA FBO:
  - Top-Left: `FRONT` (`0°, 0°`)
  - Top-Right: `SIDE +90` (`90°, 0°`)
  - Bottom-Left: `3/4 ISO` (`35°, 25°`)
  - Bottom-Right: `TOP` (`0°, 90°`)
- Composites the four tiles into the output RGB buffer with:
  - A 2 px neutral divider crosshair separating the four quadrants.
  - Crisp corner labels in each tile's top-left corner rendered with an embedded 5×7 bitmap font scaled by `max(1, tileSize / 256)` (`2×` = `10×14 px` glyphs at default `1024×1024`) over a subtle light backing pill (`#ffffff` blended softly with the tile), strictly clipped to the tile bounds so labels never bleed across quadrants.
- **Non-mutating:** running `contact-sheet` in `batch` or interactive mode does not alter the session's active camera angle, distance, pan target, or default snapshot `size`.

### 5.6 Pure PNG encoding and atomic writes (`Png.fs`)

- `Png.encodeRgb24 (width: int) (height: int) (bottomUpRgb: byte[]) : byte[]`
- `Png.encodeGray8 (width: int) (height: int) (bottomUpGray: byte[]) : byte[]`
- Writes standard PNG chunks (`IHDR`, `IDAT` compressed via `ZLibStream(CompressionLevel.Fastest)` with filter byte `0` per scanline, `IEND`), flipping OpenGL's bottom-up rows to top-down PNG scanlines during encoding with zero extra full-frame buffer copy.
- **Atomic file writes:** reject target paths that are existing directories; write to a sibling temp file `<dir>/.rubberplant-viewer.<pid>.<guid>.tmp`, then atomically replace via `File.Move(tmp, dest, overwrite = true)`. When `--stats-out` is also requested, write both temp files first and move both (cleaning up temp files on failure).

---

## 6. CLI, `batch` protocol, and interactive mode (`Protocol.fs`, `Program.fs`)

### 6.1 CLI synopsis and dispatch

```text
Usage:
  rubberplant-viewer view [file.rp] [viewer options] [load options]
  rubberplant-viewer <file.rp> [viewer options] [load options]
  rubberplant-viewer contact-sheet <file.rp> -o <out.png> [options]
  rubberplant-viewer snapshot      <file.rp> -o <out.png> [options]
  rubberplant-viewer silhouette    <file.rp> -o <out.png> [options]
  rubberplant-viewer batch
```

- **Dispatch rule:**
  - Empty `argv`: print usage to `stdout` and exit **`2`** (matching `rubberplant`).
  - `-h` / `--help`: print usage to `stdout` and exit **`0`**.
  - Explicit subcommands: `view`, `contact-sheet`, `snapshot`, `silhouette`, `batch`.
  - Convenience shorthand: if `argv[0]` ends with `.rp` (case-insensitive), treat it as `view <argv...>`. Unknown tokens like `snaphsot` fail cleanly with `unknown command` and exit **`2`**.
  - Strict subcommand rules (exit **`2`**): passing any arguments to `batch`, passing load options to `view` without a `[file.rp]`, or invoking `batch`/`view` inside `batch`/interactive stdin.
- **UTF-8 everywhere:** force UTF-8 without BOM on `stdin`/`stdout` (matching [`Program.fs`](../src/RubberPlant.Cli/Program.fs#L530-L538)).
- **Exit codes:** `0` success, `1` runtime/compile/budget/mesh/IO/GL error, `2` CLI/command usage error.

### 6.2 Options (strict command scoping)

#### Load options (shared by `view <file.rp>`, one-shot `contact-sheet`, `snapshot`, `silhouette`, and batch `load`)

Exact flag and domain parity with `rubberplant` ([`Program.fs`](../src/RubberPlant.Cli/Program.fs#L35-L60), [`Program.fs` L186–L235](../src/RubberPlant.Cli/Program.fs#L186-L235)):

| Option | Domain / Default |
|---|---|
| `-n, --derivations <n>` | int ≥ 0 |
| `--angle <degrees>` | finite float |
| `--step <d>` | finite float |
| `--width <w>` | finite float ≥ 0 |
| `--taper <percent>` | finite float in `[0, 100]` |
| `--tropism <x,y,z>` | three finite floats, not all zero |
| `--elasticity <e>` | finite float |
| `--seed <n>` | int64, `\|n\| <= 9007199254740991` (`2^53 - 1`, default `0`) |
| `--sides <n>` | int ≥ 3 (default `12`) |
| `--width-scale <k\|auto>` | finite positive float or `auto` (default `auto`) |
| `--max-modules <n\|unlimited>` | int ≥ 1 or `unlimited` (default `1000000`) |
| `--max-primitives <n\|unlimited>` | int ≥ 1 or `unlimited` (default `250000`) |

#### Viewer & render options

| Option | Value | Applies to | Notes |
|---|---|---|---|
| `-o, --output <out.png>` | file path | one-shot `contact-sheet`, `snapshot`, `silhouette` | Required on one-shot headless commands. |
| `--size <px>` | even int in `[128, 8192]` (default `1024`) | `contact-sheet` (CLI & batch) | Square `px × px` sheet. |
| `--size <w>[x<h>]` | ints in `[1, 8192]` (default `512×512`) | `snapshot`, `silhouette` (CLI & batch), `view` | Single int `N` means `N × N` (for `view`, initial window size; default `1024×768`). |
| `--camera <az,el[,dist\|fit]>` | `el ∈ [-90, 90]`, `dist > 0` | `snapshot`, `silhouette` (CLI & batch), `view` | In one-shot CLI (`snapshot`, `silhouette`, `view`), omitting `dist` auto-fits for `(az, el)`. In `batch`/interactive `snapshot`/`silhouette`, omitting `dist` keeps the current session distance (or auto-fits if unset), while `fit` auto-fits non-mutatingly. Default `35,25` auto-fitted. |
| `--wireframe` | flag | `view`, one-shot `contact-sheet`, one-shot `snapshot` | Rejected (`exit 2`) on `silhouette` and on `batch` render commands (use `wireframe on\|off` in `batch`). |
| `--no-grid` | flag | `view`, one-shot `contact-sheet`, one-shot `snapshot` | Rejected (`exit 2`) on `silhouette` and on `batch` render commands (use `grid on\|off` in `batch`). |
| `--stats` | flag | `contact-sheet`, `snapshot`, `silhouette` (CLI & batch) | Suppresses `wrote <path>` on `stdout` and writes `Stats.toJson` verbatim to `stdout` (compact by default, or 2-space indented with `--pretty`) so `json.loads(stdout)` works directly. |
| `--stats-out <file.json>` | file path | `contact-sheet`, `snapshot`, `silhouette` (CLI & batch) | Writes `Stats.toJson` verbatim (UTF-8 without BOM) atomically to `<file.json>`. Can be combined with or used without `--stats`. |
| `--pretty` | flag | `batch`/interactive `stats`, or alongside `--stats` / `--stats-out` | 2-space indented JSON; rejected (`exit 2`) otherwise. |

**Output and JSON parity contract:**
- Without `--stats`, every render command (`contact-sheet`, `snapshot`, `silhouette`, in both one-shot CLI and `batch`) prints `wrote <path>` on `stdout` for each file written (PNG first, then `--stats-out` if given), matching `rubberplant render`.
- With `--stats`, `wrote <path>` lines are suppressed on `stdout` and `Stats.toJson pretty stats` is written verbatim via `Console.Out.Write` (never `printfn`, since `Stats.toJson` already terminates with a single LF `\n`, matching [`Program.fs` L404](../src/RubberPlant.Cli/Program.fs#L404)), guaranteeing byte-identical UTF-8 output to `rubberplant stats` across OSes.
- Warnings and diagnostics always go to `stderr` using `Diagnostic.format`, keeping `stdout` clean for JSON and `batch` framing.

### 6.3 Unified `batch` and interactive stdin protocol

Uses the exact same line splitter (`splitLine`: whitespace-separated, `"..."` quoting, backslashes literal, no NUL bytes) and `exit <code>` framing as `rubberplant batch` ([`Program.fs`](../src/RubberPlant.Cli/Program.fs#L472-L523)).

| Command | Behavior |
|---|---|
| `load <file.rp> [load options] [--keep-camera]` | Reads, compiles, derives, analyzes `Stats`, builds `Mesh`, and uploads to GPU. Auto-fits camera at `3/4 ISO (35°, 25°)` with `target = (0, 0, 0)` unless `--keep-camera` is set (which preserves `(az, el, dist, target)` in local GPU coords) or camera angles were explicitly set before the first `load`. Caches the `load` spec for `reload`. On failure (`exit 1`), leaves the previous scene, cached `Stats`, and cached `load` spec intact. |
| `reload [--keep-camera]` | Re-runs the cached `load` spec from disk (`--keep-camera` preserves `(az, el, dist, target)`). Fails with `exit 1` if no model is loaded. |
| `camera <az> <el> [dist\|fit]` | Sets orbit angles (`el ∈ [-90, 90]`). If `dist` is given, sets exact distance (`> 0`) and preserves `target`; if `fit` is given, resets `target = (0, 0, 0)` and auto-fits at `(az, el)`; if omitted, keeps current `dist` and `target` (or auto-fits if no distance has been set yet). Allowed before `load`. |
| `fit` | Resets pan offset `target = (0, 0, 0)` and auto-fits `dist` to the loaded mesh at the current `(az, el)`. Fails with `exit 1` if no model is loaded. |
| `size <w> [h]` | Sets default `snapshot`/`silhouette` resolution (`size N` = `N × N`; `w, h ∈ [1, 8192]`; default `512×512`). Does not affect `contact-sheet`. Allowed before `load`. |
| `wireframe on\|off` | Toggles wireframe overlay. Allowed before `load`. |
| `grid on\|off` | Toggles ground grid. Allowed before `load`. |
| `snapshot <out.png> [--camera <az,el[,dist\|fit]>] [--size <w>[x<h>]] [--stats] [--pretty] [--stats-out <path>]` | Renders shaded PNG. Optional `--camera` and `--size` are non-mutating. Fails with `exit 1` if no model is loaded. |
| `contact-sheet <out.png> [--size <px>] [--stats] [--pretty] [--stats-out <path>]` | Renders 2×2 shared-scale PNG without mutating session camera or `size`. Fails with `exit 1` if no model is loaded. |
| `silhouette <out.png> [--camera <az,el[,dist\|fit]>] [--size <w>[x<h>]] [--stats] [--pretty] [--stats-out <path>]` | Renders 1× non-MSAA binary Gray8 PNG (ignoring `grid`/`wireframe`). Optional `--camera` and `--size` are non-mutating. Fails with `exit 1` if no model is loaded. |
| `stats [--pretty]` | Writes cached `Stats.toJson` verbatim to `stdout` (byte-identical to `rubberplant stats`). Fails with `exit 1` if no model is loaded. |
| `quit` | Prints `exit 0` (in both `batch` and interactive stdin modes), flushes `stdout`, and exits `0`. |

- **Output framing:** after every command, flush `stderr`, print `exit <code>` on `stdout`, and flush `stdout`. EOF on `stdin` exits `0` in `batch` mode (and simply stops listening for stdin commands in interactive `view` mode without closing the window).
- **Blank lines:** matching `rubberplant batch`, an empty line parses to `[]` and returns `missing command` (`exit 2`), keeping one shared framing rule across both tools.

### 6.4 Interactive window mode (`view`)

- **Mouse controls:**
  - Left-drag: orbit (`az`, `el` clamped to `[-90°, 90°]`).
  - Right-drag, Middle-drag, or Shift + Left-drag: pan `target` along camera `right`/`up` scaled by `distance`.
  - Scroll wheel: exponential zoom (`distance *= 0.9^delta`, clamped to a safe positive range).
- **Keyboard shortcuts:**
  - `1` Front (`0°, 0°`), `2` Side +90° (`90°, 0°`), `3` 3/4 Iso (`35°, 25°`), `4` Top-Down (`0°, 90°`) — each switches orientation, resets pan `target = (0, 0, 0)`, and re-fits.
  - `F`: fit camera (resets pan `target = (0, 0, 0)`).
  - `W`: toggle wireframe overlay.
  - `G`: toggle ground grid.
  - `R`: reload current `.rp` file from disk (keeping current camera).
  - `Esc` / `Q`: close window and exit `0`.
- **Window title:** `RubberPlant — <filename>` (or `RubberPlant — (no model)`).
- **Minimized window handling:** when `glfwGetFramebufferSize` reports `fbW = 0` or `fbH = 0` (minimized window on Windows), skip rendering to the default framebuffer while continuing to process GLFW events and offscreen stdin commands cleanly.
- **Event loop & live stdin bridge:**
  - Main thread owns the GLFW window and OpenGL context and blocks in `glfwWaitEvents()` (zero CPU when idle), redrawing only when marked dirty (input event, resize, or command execution).
  - A background reader thread (`IsBackground = true`, so closing the window from a terminal never hangs waiting on `Console.In.ReadLine()`) reads UTF-8 lines from `stdin`, enqueues them onto a `ConcurrentQueue<string>`, and calls `glfwPostEmptyEvent()` to wake the main thread immediately.
  - On each wakeup, the main thread drains the queue, executes each command on the OpenGL thread (printing output + `exit <code>` to `stdout`), restores the default framebuffer and physical HiDPI viewport (`glfwGetFramebufferSize`), and redraws if `fbW > 0 && fbH > 0`.

---

## 7. Testing, CI, and PR breakdown

### 7.1 Three-layer verification

1. **Layer 1 — Pure Expecto unit tests (`tests/RubberPlant.Viewer.Tests`, zero GPU):**
   - `Camera`: pole-free basis orthonormality and right-handedness across `az ∈ [-360, 360]` and `el ∈ {-90, -45, 0, 25, 90}`, aspect-aware 8-corner `fit` (tall, wide, square, 2D planar, degenerate 1D stick, empty scene), shared-scale contact-sheet distance, bounded-ratio `near`/`far` (`far / near <= 10,000`) including grid visibility and deep zoom-in.
   - `Scene`: double-precision origin centering, `uint32` index offsetting across multiple `MeshGroup`s, `float32` span overflow error, empty mesh & degenerate-only mesh handling, ground grid step selection and local-frame shift (including `y < 0` models and huge `|center.Y|` omission).
   - `Png`: pure-F# PNG decoder round-trip verifying signature, `IHDR`, CRC-32, `IDAT` decompression, vertical flip, RGB24 & Gray8 pixel exactness, odd widths, and 2×2 contact-sheet divider/label composition.
   - `Protocol`: `splitLine` parity with `RubberPlant.Cli`, CLI dispatch (`view`, `.rp` shorthand, unknown commands, empty args), strict flag scoping (`--pretty`, `--wireframe`, `--no-grid`), domain validation (`el`, `dist`, even contact-sheet `--size`), pre-`load` errors, and transactional `load`/`reload` state preservation.
   - Automatic `CliTests.aotFormatTests` scan covering `src/RubberPlant.Viewer/**/*.fs`.

2. **Layer 2 — Headless GPU integration tests (local RTX 4070 + CI Mesa):**
   - Hardware-independent invariants (no fragile GPU pixel-hash goldens across drivers):
     - Valid PNG signatures, dimensions, and CRCs for `snapshot`, `contact-sheet`, and `silhouette`.
     - Odd-width readback (`--size 301`) proving `GL_PACK_ALIGNMENT = 1`.
     - Non-blank palette pixels on 3D trees and `fig2-8b.rp` (proving 32-bit indices > 65,535 vertices).
     - Pure background on `lsystem-1-7.rp` (empty) and degenerate-only `.rp` fixtures.
     - All four contact-sheet quadrants contain foreground pixels on 3D trees; corner labels present.
     - `silhouette` output is strictly 8-bit grayscale with every pixel in `{0, 255}`, and ignores active `grid`/`wireframe` toggles.
     - One-shot `contact-sheet ... --stats` emits valid compact JSON on `stdout` byte-identical to `rubberplant stats`, and `--stats-out` writes the same JSON atomically.
     - `batch` recovery: `RP0206`/`RP0207` budget failure returns `exit 1`, leaves previous loaded model intact, and subsequent `snapshot`/`stats` commands succeed with `exit 0`.

3. **Layer 3 — Windows Native AOT + RTX 4070 performance check:**
   - CI adds `RubberPlant.Viewer.Tests` to the test loop (under `xvfb-run -a` on `ubuntu-latest` and `ssciwr/setup-mesa-dist-win@v3` on `windows-latest`) and publishes `src/RubberPlant.Viewer` with `<PublishAot>true</PublishAot>` on `windows-latest`, running a headless batch smoke test against the AOT binary.
   - Locally on RTX 4070, run headless `batch` 2×2 contact sheets across all 40 `examples/*.rp` and verify total wall-clock time is a few seconds (Battle Plan Exit Criterion 2).

### 7.2 PR sequence

| PR | Branch | Scope |
|---|---|---|
| **PR 1** | `joce/bp2-spec` | Author `docs/viewer.md` and link from [`docs/README.md`](README.md). |
| **PR 2** | `joce/bp2-viewer-core` | Project/solution scaffolding, **early `Silk.NET` Native AOT smoke test** (locking primary vs P/Invoke fallback), pure modules (`Camera.fs`, `Scene.fs`, `Png.fs`, `Protocol.fs`), and Layer 1 unit tests. |
| **PR 3** | `joce/bp2-viewer-headless` | OpenGL 3.3 Core (`GlContext.fs`, `Shaders.fs`, `Renderer.fs`), one-shot + `batch` modes, Layer 2 GPU tests, CI Mesa + Windows Native AOT publish integration, 40-example RTX 4070 benchmark. |
| **PR 4** | `joce/bp2-viewer-interactive` | Interactive `view` window (orbit/pan/zoom, shortcuts, HiDPI, `IsBackground = true` stdin bridge + `glfwPostEmptyEvent()`), `README.md` documentation. |
