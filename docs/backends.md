# Geometry and output backends

Status: **v0.2, partly implemented**. Implemented: `Palette` and `Mesh` (§4), OBJ (§5), the
SVG backend's 0.2 changes (§7), the CLI (§8) for SVG and OBJ, the Chapter 2 examples and the
mesh fixtures with their OBJ goldens (§9). Not yet: glTF (§6) and its CLI formats, the glTF
goldens, the mesh digests, the glTF validator in CI, and the Chapter 3 examples (§9). This document specifies
what the 0.2 milestone adds after the turtle: the meaning of the geometry in 3D, a shared mesh
builder, the OBJ and glTF backends, the CLI changes, and how all of it is tested. The language side
(`$`, tropism, `%`, `consider:`, `G`, `.`) is in [`language.md`](language.md).

---

## 1. Scope

| In 0.2 | Not in 0.2 |
|---|---|
| Segments rendered as cylinders in 3D | Generalized cylinders (Ch 5, 0.3) |
| Flat, two-sided polygons, concave allowed | Predefined surfaces `~` (Ch 5, 0.3) |
| OBJ (+ MTL), glTF (`.gltf` and `.glb`) | Textures, animation, cameras, lights |
| SVG keeps its front view of 3D models | Other SVG views, depth sorting |
| Backend chosen from the output file extension | Several outputs in one run |

Architecture stays as in 0.1: every backend is a **pure function** from `Geometry` (plus options) to
output text or bytes, and the CLI does all the I/O.

```
Geometry ──► Svg.render                          ──► string
         └─► Mesh.build ──► Obj.render           ──► { Obj: string; Mtl: string }
                        └─► Gltf.renderText      ──► string   (.gltf)
                        └─► Gltf.renderBinary    ──► byte[]   (.glb)
```

`Mesh.build` turns geometry into triangles once, so OBJ and glTF contain exactly the same mesh. It
lives in `RubberPlant.Backends` (`Mesh.fs`), next to the backends that use it: tessellation is a
rendering decision, not part of the model.

## 2. World coordinates and units

- Right-handed, **+Y up**, +X right, +Z toward a front viewer (`language.md` §5.1). The turtle starts
  at the origin facing +Y. This is glTF's convention, so no axis conversion happens anywhere.
- Mesh backends write turtle coordinates **unscaled**: one step of `F(1)` is one unit (a meter, for
  glTF). Viewers frame the model themselves. SVG still fits the drawing to its canvas.
- Widths are *width units* (`language.md` §5.4): pixels in SVG, world units × **width scale** in
  meshes (§4.4).

## 3. Geometry

The `Geometry` type doesn't change in 0.2:

```fsharp
type Primitive =
    | Segment of p0: Vec3 * p1: Vec3 * width: float * color: int
    | Polygon of vertices: Vec3 list * color: int
```

What changes is what it means in 3D:

- **`Segment` is an open cylinder**, a tube without end caps, of diameter `width` from p0 to p1.
  This is the 3D segment of ABOP's own software (cpfg's and lpfg's `line style: cylinder`, and
  lpfg's OBJ export); the book's figures are line drawings or renders made with it. A zero-length or zero-width segment is invisible.
- The SVG backend stays a **line drawing**, with straight (butt) line ends like the book's figures.
  It isn't meant to be an exact projection of the mesh (§7).
- **`Polygon`** is a flat, filled, two-sided face. Its vertices may be non-planar, and the polygon may
  be concave (ABOP's leaves are). Self-intersecting polygons are triangulated on a best-effort basis
  (§4.3).

The turtle guarantees finite coordinates (RP0205), so backends never check their input for NaN or
infinity. (`Mesh.build` still checks its output, §4.)

## 4. Mesh construction (`Mesh.build`)

```fsharp
type MeshOptions =
    { /// Facets around a cylinder; at least 3.
      Sides: int
      /// World units per width unit; None means "auto" (§4.4).
      WidthScale: float option }

let defaultMeshOptions = { Sides = 12; WidthScale = None }

type MeshGroup =
    { /// Palette entry, 0-7 (§4.5).
      Color: int
      Positions: Vec3[]
      Normals: Vec3[]          // unit length, one per position
      Triangles: int[] }       // 3 indices per triangle, counter-clockwise seen from outside

type Mesh = { Groups: MeshGroup list }   // ascending Color; colors with no triangles are omitted

val build : MeshOptions -> Geometry -> Result<Mesh, string>
```

Primitives are grouped **by palette entry** (the color index wrapped into the palette, §4.5), in
ascending order: color indices that look the same share a group. Within a group, primitives keep their
geometry order, and each primitive appends its own vertices: nothing is shared or deduplicated
between primitives. The result is fully deterministic.

Lengths and normalizations are computed on vectors scaled by a power of two, so they neither
overflow nor underflow for any finite geometry (scaling by a power of two is exact). Polygons are
triangulated in a **local frame** (§4.3): their vertices relative to the loop's first vertex,
scaled by a power of two from the loop's own size, so the result depends neither on where the
polygon is nor on how big it is. If a vertex still comes out non-finite (a cylinder's radius or a
coordinate near the limits of double precision), `build` fails.

### 4.1 Cylinders

For `Segment(p0, p1, w, c)` with width scale *k* (§4.4):

- Radius *r* = *k* · *w* / 2 and length ℓ = |p1 − p0|. If *r* = 0 or ℓ = 0, the segment produces
  no triangles.
- Axis **n** = (p1 − p0) / ℓ.
- Cross-section basis: let **a** be the world axis (X, Y, then Z on ties) with the smallest
  |**a** · **n**|. Then **e1** = normalize(**n** × **a**) and **e2** = **n** × **e1**. (cpfg and lpfg
  align the cross-section with the turtle's left and up vectors instead. That would require storing
  the turtle's orientation in `Segment`, and with 12 sides the difference is barely visible.)
- *s* = `Sides` facets around (default 12, CLI `--sides`). The same number for every cylinder in
  0.2; a later version may pick it from the radius, so a trunk gets many sides and a twig 4 or 6.
  Direction *j* (0 … *s* − 1) is
  **d**<sub>j</sub> = cos(360° *j* / *s*) **e1** + sin(360° *j* / *s*) **e2**.
- **Vertices**, 2*s* in this order: the ring at p0 (p0 + *r* **d**<sub>j</sub>), then the ring at p1
  (p1 + *r* **d**<sub>j</sub>). Vertex *j* of each ring has the normal **d**<sub>j</sub>, so the tube is
  smoothly shaded.
- **Triangles**, 2*s*: two per side quad, counter-clockwise seen from outside. For *j* = 0 … *s* − 1
  in order, with *j*′ = (*j* + 1) mod *s*, the triangles are (*j*, *j*′, *s* + *j*′) and
  (*j*, *s* + *j*′, *s* + *j*), as indices into the cylinder's own vertices. (**e1**, **e2**, **n**)
  is right-handed, so going from *j* to *j*′ turns counter-clockwise around **n**, and both
  triangles face outward.
- **No end caps**, as in cpfg and lpfg: the end of a branch tip is open, which shows only from
  close up.
- Per cylinder: 24 vertices and 24 triangles with the defaults.
- **Constant width**: the radius is the same at both ends. cpfg tapers each cylinder by default, to
  the width where the next segment starts; tapering comes later, with generalized cylinders (Ch 5).

Cylinders aren't joined. Where an axis bends, consecutive cylinders overlap on the inside of the bend
and leave a small wedge-shaped gap on the outside, as cpfg's and lpfg's cylinders do. The gap grows
with the bend angle and the width; it is small for the gentle bends of tropism. Generalized cylinders
(Ch 5, 0.3) remove it.

### 4.2 Line widths are cylinder diameters

`!` and `width` already hold the diameter, so the turtle doesn't change. ABOP's da Vinci rule
(Fig 2.6, *w*<sub>r</sub> = 0.707) relates diameters, which is consistent with this choice.

### 4.3 Polygons

For `Polygon(vs, c)`:

1. Drop consecutive duplicate vertices (exact equality), then drop the last vertex while it equals
   the first. What is left is the **loop**.
2. Fewer than 3 vertices left: no triangles.

Steps 3 to 5 work in the loop's **local frame**: each vertex minus the loop's first vertex,
multiplied by 2<sup>−*e*</sup>, where 2<sup>*e*</sup> ≤ (the largest absolute coordinate of
those differences) < 2<sup>*e*+1</sup>. If a difference overflows (a loop spanning nearly the
whole double range), the vertices are halved before the subtraction instead; only that case pays
for it (halving can round subnormal coordinates).

3. Compute the **Newell normal** **N** of the loop (in the local frame). Let *E* be the loop's
   largest extent along X, Y or Z (max − min of that coordinate, in the local frame). If |**N**| < 10<sup>−12</sup> × *E*<sup>2</sup>, the
   polygon has no area: no triangles.
4. Project the loop onto the coordinate plane perpendicular to **N**'s largest component (in
   absolute value; X, Y, then Z on ties). The 2D coordinates (*u*, *v*) are the other two axes in
   cyclic order ((Y, Z) when dropping X, (Z, X) when dropping Y, (X, Y) when dropping Z), swapped
   when the dropped component of **N** is negative. This keeps the loop counter-clockwise around
   **N**.
5. **Ear clipping**, in the projection, on the *remaining loop* (initially the whole loop):
   - While more than 3 vertices remain, scan them from the first remaining vertex and clip the
     first ear found: emit its triangle and remove its middle vertex from the remaining loop. Each
     scan starts again from the first remaining vertex.
   - A remaining vertex *v*, with *p* before it and *q* after it in the remaining loop (cyclically),
     is an **ear** when the turn *p*, *v*, *q* is strictly counter-clockwise (exact sign of the 2D
     cross product, no tolerance), and no other remaining vertex lies inside the triangle
     (*p*, *v*, *q*) or on its edges. Remaining vertices at the same 2D position as *p*, *v* or *q*
     don't count, so a loop that touches itself at a vertex can still be clipped.
   - When 3 vertices remain, they form the last triangle. If no ear can be found (a
     self-intersecting or degenerate loop), what remains is triangulated as a fan from the first
     remaining vertex. The last triangle and the fan's triangles are emitted only if their area
     in the projection isn't zero, and with their last two vertices swapped if it is negative
     (which happens only in self-intersecting loops), so every triangle turns counter-clockwise.
     (An ear's area is never zero. Zero-area leftovers are common: a leaf drawn with `F F F` has
     collinear vertices along its edges.)
6. The polygon's vertices are the loop's (in world coordinates), in loop order, each with the normal **N** / |**N**| (flat
   shading). Triangles are counter-clockwise around **N**.

A primitive that produces no triangles, polygon or segment, adds no vertices.

Polygons are **two-sided**, and back faces aren't duplicated. glTF says so in the file: its
materials set `doubleSided`. OBJ and MTL have no standard way to say it, and viewers differ: some
draw both sides by default, others cull back faces (three.js's `MTLLoader` makes front-side
materials, for example). To see leaves from behind in such a viewer, turn off back-face culling
or make the materials double-sided (§5).

### 4.4 Width scale

ABOP gives widths as line widths in pixels: Fig 2.6 starts with a trunk 1 unit long and 10 wide.
Taken as world units, that trunk would be a blob. Mesh backends therefore multiply widths by a
**width scale** *k*:

- **Explicit**: `WidthScale = Some k`, with *k* finite and positive (CLI `--width-scale <k>`).
- **Auto** (the default): *k* makes the mesh match the SVG that the default options would draw.
  Take *E* = max(x extent, y extent) of all geometry points (the SVG's front view), *W* = the largest
  segment width, and the SVG defaults *size* = 800 and *margin* = 10. Then
  *k* = *E* / (*size* − 2 (*margin* + *W* / 2)). If the denominator isn't positive, which is the
  case where the SVG backend would refuse to render, `build` fails and suggests `--width-scale`;
  this is checked first. Otherwise *k* = 1 if *E* = 0.

So by default a cylinder appears as thick, relative to the plant, as the line in the SVG. The cost is
that *k* depends on the extent of the drawing. That's harmless for still images, and animation
(Ch 6) will need a fixed *k* anyway.

### 4.5 Colors

The color palette moves out of `Svg.fs` into a shared `Palette` module, unchanged: 8 sRGB colors,
indexed modulo 8 (negative indices wrap too). SVG, MTL and glTF all use it, so a color index looks
the same in every backend.

## 5. OBJ backend

```fsharp
type ObjOutput = { Obj: string; Mtl: string }
val render : MeshOptions -> mtlFileName: string -> Geometry -> Result<ObjOutput, string>
```

`.obj` text, LF line endings:

```
# RubberPlant
mtllib tree.mtl
v 0 0 0
…                       (all positions, groups in order)
vn 0 -1 0
…                       (all normals, same order: normal i belongs to position i)
usemtl color1
f 1//1 2//2 3//3
…                       (faces of the group, 1-based global indices)
usemtl color2
…
```

OBJ itself only stores geometry. Its standard way to color faces is a companion **MTL**
("material template library") file: the OBJ names it (`mtllib tree.mtl`) and switches material
before each group of faces (`usemtl color1`), and the MTL defines each material's color. Every OBJ
reader understands it, and lpfg's own OBJ export works the same way (`objout.cpp`). Per-vertex
colors (`v x y z r g b`) would keep a single file, but they aren't part of the OBJ format and many
programs ignore or reject them. For a single file with colors, use glTF.

`.mtl` text, one material per group, in the same order, separated by a blank line:

```
newmtl color1
Kd 0.180392 0.490196 0.196078
illum 1

newmtl color2
Kd 0.427451 0.298039 0.254902
illum 1
```

- Materials are named `color<i>`, where *i* is the group's palette entry (0–7). `Kd` is the sRGB
  color / 255 (MTL has no color management).
- Numbers (`v`, `vn`, `Kd`): the double's exact binary value rounded to 6 decimals, halves to
  even (IEEE's default rounding; .NET's `F6` format); invariant culture, no exponent, trailing
  zeros and a trailing point removed, and `-0` written `0`.
- Both files use LF line endings, and every line ends with one, the last included. There are no
  blank lines in the OBJ.
- The OBJ refers to the MTL by `mtlFileName`, a bare file name that the CLI derives from the output
  path. The function itself does no I/O.
- Empty geometry: the header and `mtllib` lines only, and an empty MTL.
- Two-sided faces: OBJ can't mark faces or materials as two-sided, so importers that cull back
  faces hide leaves seen from behind; disable back-face culling (or use double-sided materials)
  there (§4.3). glTF doesn't have this problem.

## 6. glTF backend

```fsharp
val renderText   : MeshOptions -> Geometry -> Result<string, string>   // .gltf, buffer embedded
val renderBinary : MeshOptions -> Geometry -> Result<byte[], string>   // .glb
```

glTF comes in two file formats with the same content, and both are produced:

- **`.gltf`** is a JSON text file. The mesh data (positions, normals, indices) is binary, so it
  normally lives in a separate `.bin` file next to the JSON. Here it is embedded in the JSON instead,
  encoded as base64 text in a `data:` URI, so the output is still a single file.
- **`.glb`** is the binary form: one file holding the same JSON and the raw binary data side by side.
  It is about 25% smaller (no base64) and is what most viewers and engines expect.

The two are built from the same document, so supporting `.glb` costs a few dozen lines. Tests use
the `.gltf` form because it is text.

- `asset`: `{ "version": "2.0", "generator": "RubberPlant" }`. The version number is left out on
  purpose, so a release doesn't change every output.
- One scene with one node and one mesh. The mesh has one **primitive per group**: attributes
  `POSITION` and `NORMAL` (float32 VEC3), `indices` (uint32 SCALAR), mode 4 (triangles), and the
  group's material. `POSITION` has its required `min` and `max`.
- **Buffer layout**: one buffer. For each group in order: positions, normals, indices. Each gets its
  own bufferView (`target` 34962 for attributes, 34963 for indices), 4-byte aligned.
- **Materials**: one per group, named `color<i>`. `pbrMetallicRoughness`:
  `baseColorFactor` is the palette color converted from sRGB to **linear** (glTF requires linear),
  with alpha 1; `metallicFactor` 0; `roughnessFactor` 1. `doubleSided`: true.
- Empty geometry: a scene with one node and no mesh, no buffer.
- JSON: fixed key order, 2-space indentation, LF, numbers in shortest round-trip form (float32 values
  formatted as float32). `.glb` pads the JSON chunk with spaces and the BIN chunk with zeros.

## 7. SVG backend in 0.2

- Line ends change from round to straight (butt), so every golden with segments changes once.
- 3D models are drawn as before: an orthographic front view (looking down −Z), primitives painted
  in geometry order, with no depth sorting. ABOP chose a view for each of its line drawings of 3D
  trees: Figs 2.6a–c and 2.8b–d are front views, Figs 2.6d and 2.7 are seen from +X (their examples
  turn the tree with `/(90)`), and Fig 2.8a from below (not reproduced; `language.md` §7).
- Segments have **straight (butt) ends**, like the lines of ABOP's figures (drawn by cpfg). A round
  cap would stick out half a line width past the end of every segment, which shows on thick
  trunks and at every change of width. Consecutive segments with the same width and color still
  form one path with **round joins**, so bends have no notches; where the width or color changes,
  the lines meet end to end and the width steps, as in the book. SVG is a line drawing, like the
  book's figures, not a projection of the mesh.
- The palette moves to the shared `Palette` module (§4.5).

## 8. CLI

```
rubberplant render <file.rp> [-o <out>] [--format svg|obj|gltf|glb] [options]
```

- **Format from the extension** of `-o`: `.svg`, `.obj`, `.gltf`, `.glb` (case-insensitive). Any
  other extension is an error.
- Without `-o`, the output is the input file with the extension of `--format` (default `svg`), as in
  0.1. Giving both `-o` and a `--format` that disagrees with it is an error.
- OBJ writes two files: `tree.obj`, and `tree.mtl` next to it (`mtllib tree.mtl`). The CLI prints
  both paths.
- New options:

  | Option | Applies to | Meaning |
  |---|---|---|
  | `--format <f>` | render | `svg`, `obj`, `gltf` or `glb`, when there's no `-o` |
  | `--sides <n>` | obj, gltf, glb | cylinder facets, integer ≥ 3 (default 12) |
  | `--width-scale <k\|auto>` | obj, gltf, glb | width scale, finite and > 0, or `auto` (default) |
  | `--tropism <x,y,z>` | all | overrides the `tropism` setting |
  | `--elasticity <e>` | all | overrides the `elasticity` setting |

- An option that doesn't apply to the chosen format (`--size` with OBJ, `--sides` with SVG) is a
  usage error, not silently ignored. So is an option that doesn't apply to the command: `-o`,
  `--format`, `--size`, `--sides` and `--width-scale` with `derive` or `check`, and `--drawing`
  with anything but `derive`.
- Writing is all or nothing: the CLI first checks that every output file can be written, and if
  writing one still fails, it restores the files it already replaced. A failed OBJ render never
  leaves an OBJ next to an MTL it doesn't match.
- For each file written, the CLI prints `wrote <path>`. A mesh that can't be built (`Mesh.build`
  fails, §4) is an error, like an SVG that can't be drawn.

## 9. Tests and goldens

Existing machinery stays: every `examples/*.rp` has a golden SVG, compared byte for byte by the
Backends tests and by `scripts/check-goldens.sh`, and regenerated with `RUBBERPLANT_UPDATE_GOLDEN=1`.

New in 0.2:

- **Core tests** for each language feature: `$` (including vertical H), tropism (angle, axis, zero
  **T**, *e* = 0), the cut (one step late, nested branches, top level, axiom, `%` rejected in `as` successors), `consider:`
  (including with `*`, and the error when combined with `ignore:`), `G`, `.`, `{` starting an
  empty polygon, the empty successor, vector settings and their parse errors.
- **Book derivation sequences as tests**: ABOP prints some developmental sequences in full, and
  `derive` must reproduce them exactly. These are the signal model of §3.1.2 (*d* = 4, *m* = 2,
  *u* = 1, 10 strings) and the lily-of-the-valley sequence `A`, `I[IK0]A`, … of §3.3.1, written with
  parametric `K(i)`. The signal model's strings 9 and 10 as printed on p. 68 are a book erratum:
  they don't follow from its productions (p8 turns `a(2)` into `I[L]A`, adding a fourth internode).
  The test keeps the book's productions, matches strings 1–8 as printed, and expects what the
  productions derive for strings 9 and 10.
- **Mesh unit tests**: vertex and triangle counts per cylinder, unit normals, outward winding (each
  triangle's normal points away from the axis), zero-length and zero-width segments (no triangles), ear clipping of a
  concave polygon (an L shape), and degenerate polygons.
- **Mesh golden fixtures**: a few small hand-written geometries (one cylinder, a bent chain, a concave
  leaf, two colors), with full `.obj`, `.mtl` and `.gltf` goldens in `golden/mesh/`. Their diffs are
  small enough to review.
- **Mesh digests for every example**: full meshes of the large trees run to megabytes (Fig 2.8b has
  about 13,000 cylinders), too big to check in. `golden/mesh-digests.txt` lists
  `<example> <sha256 of obj+mtl> <sha256 of gltf>` with default options. The tests and
  `check-goldens.sh` compare them, and `RUBBERPLANT_UPDATE_GOLDEN=1` rewrites them.
- **glTF validation**: CI runs the Khronos glTF Validator on every example's `.gltf` and fails on
  errors. This adds Node (`npx gltf-validator`) to CI.
- `seeds.txt` stays as it is.

### Examples for 0.2

| Example | Source | Needs |
|---|---|---|
| `fig2-6a` … `fig2-6d` | Fig 2.6, Table 2.1 | `$`; 2.6b uses r1 = 0.8 (Table 2.1 prints 0.9, a probable misprint); 2.6d starts with `/(90)` (seen from +X) |
| `fig2-7a` … `fig2-7d` | Fig 2.7, Table 2.2 | `$`; start with `/(90)` (seen from +X) |
| `fig2-8a` … `fig2-8d` | Fig 2.8, Table 2.3 | tropism; 2.8c uses lr = 1.079 (Table 2.3 prints 1.790, a probable misprint); the book's 2.8a is seen from below, and its 2.8c–d couldn't be reproduced exactly, even with cpfg |
| `fig3-5-capsella` | Fig 3.5 | `.`, `G`, `%`, polygons |
| `fig3-17-mycelis` | L-system 3.2 (model II) | `G`, `ignore:`; stand-in for `~O` |
| `mycelis-model3` | L-system 3.3 | `consider:`, `G`; stand-in for `K` |
| `fig3-9-dibotryoid`, `fig3-10-dibotryoid` | L-system 3.1, with the figures' constants | added `as` rules for `I`, `L`, `K` (the book gives no geometry) |
| `fig3-2-crocus` | Fig 3.2 | stand-ins for `~L(t)`, `~K(t)` |
| `fig3-14-lychnis` | Fig 3.14 | stand-ins for `~L(t)`, `~K(t)` |
| `fig3-19-lilac` | Fig 3.19 | stand-in for `~K` |

Each example renders one developmental stage. Where the book shows several stages, the file says
which one it uses. Every stand-in and every added `as` rule is marked with a comment: these are
RubberPlant additions, not the book's.

## 10. Suggested PR order

1. Spec (this document and `language.md`).
2. Language: `consider:`, vector settings with `tropism` and `elasticity`, `%` (parser and cut),
   the empty successor, plus the derivation-sequence tests.
3. Turtle: `$`, tropism, `G`, `.`, `{` starting an empty polygon (updates the Ch 1 goldens' vertex
   order).
4. Chapter 2 examples (SVG goldens).
5. `Palette` and `Mesh`, with unit tests and fixtures.
6. OBJ backend and CLI format selection.
7. glTF backend (`.gltf`, `.glb`), digests, validator in CI.
8. Chapter 3 examples.
