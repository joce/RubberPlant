module RubberPlant.Viewer.Tests.RendererTests

open System
open System.Collections.Concurrent
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading
open Expecto
open RubberPlant.Viewer

let private repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))
let private examplePath name = Path.Combine(repoRoot, "examples", name)

let private withTempDir (f: string -> unit) =
    let dir = Path.Combine(Path.GetTempPath(), "rubberplant-viewer-test-" + Path.GetRandomFileName())
    Directory.CreateDirectory dir |> ignore

    try
        f dir
    finally
        for file in Directory.GetFiles dir do
            File.SetAttributes(file, FileAttributes.Normal)

        Directory.Delete(dir, true)

let private runCli (args: string list) =
    use stdin = new StringReader("")
    use stdout = new StringWriter()
    use stderr = new StringWriter()
    let code = Program.executeCli args stdin stdout stderr
    code, stdout.ToString(), stderr.ToString()

let private runBatch (lines: string list) =
    use stdin = new StringReader(String.concat "\n" lines + "\n")
    use stdout = new StringWriter()
    use stderr = new StringWriter()
    let code = Program.executeBatch stdin stdout stderr
    code, stdout.ToString(), stderr.ToString()

type private AsyncQueueTextReader(lines: BlockingCollection<string>) =
    inherit TextReader()

    override _.ReadLine() =
        try
            lines.Take()
        with
        | :? InvalidOperationException -> null
        | :? OperationCanceledException -> null

type private SignallingTextWriter(onLine: string -> unit) =
    inherit TextWriter()
    let sb = StringBuilder()
    let all = StringBuilder()
    let gate = obj ()

    override _.Encoding = Encoding.UTF8

    override _.Write(value: char) =
        lock gate (fun () ->
            all.Append(value) |> ignore

            if value = '\n' then
                let line = sb.ToString().TrimEnd('\r')
                sb.Clear() |> ignore
                onLine line
            else
                sb.Append(value) |> ignore)

    override this.Write(value: string) =
        if not (isNull value) then
            for ch in value do
                this.Write(ch)

    member _.AllText() = lock gate (fun () -> all.ToString())

let private sha256Hex (text: string) =
    let bytes = UTF8Encoding(false).GetBytes(text)
    use sha = SHA256.Create()
    let hash = sha.ComputeHash(bytes)
    BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()

let private splitNonEmptyLines (text: string) =
    text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)

let private countNonBackgroundRgb (dec: PngTests.DecodedPng) =
    let mutable count = 0

    for i = 0 to dec.Width * dec.Height - 1 do
        let o = i * 3

        if dec.TopDownPixels[o] <> 242uy || dec.TopDownPixels[o + 1] <> 242uy || dec.TopDownPixels[o + 2] <> 240uy then
            count <- count + 1

    count

let private pixelRgb (dec: PngTests.DecodedPng) (x: int) (yTop: int) =
    let o = (yTop * dec.Width + x) * 3
    dec.TopDownPixels[o], dec.TopDownPixels[o + 1], dec.TopDownPixels[o + 2]

[<Tests>]
let architectureTests =
    test "Silk.NET, GlContext, and Shaders are confined strictly to the OpenGL backend boundary" {
        let viewerSrc = Path.Combine(repoRoot, "src", "RubberPlant.Viewer")
        let files = Directory.GetFiles(viewerSrc, "*.fs", SearchOption.AllDirectories)
        let allowed = set [ "GlContext.fs"; "Shaders.fs"; "Renderer.fs" ]

        let offenders =
            [ for file in files do
                  let name = Path.GetFileName file

                  if not (allowed.Contains name) then
                      let text = File.ReadAllText file

                      if
                          text.Contains("Silk.NET", StringComparison.OrdinalIgnoreCase)
                          || text.Contains("GlContext", StringComparison.Ordinal)
                          || text.Contains("Shaders", StringComparison.Ordinal)
                      then
                          name ]

        Expect.isNonEmpty files "viewer source files found"
        Expect.isEmpty offenders "only GlContext.fs, Shaders.fs, and Renderer.fs may reference Silk.NET, GlContext, or Shaders"
    }

[<Tests>]
let gpuTests =
    testSequenced
    <| testList
        "Renderer (GPU)"
        [ test "snapshot, contact-sheet, and silhouette produce valid PNGs with foreground pixels on 3D trees and colored plants" {
              withTempDir (fun dir ->
                  let fig26a = examplePath "fig2-6a.rp"
                  let snapPath = Path.Combine(dir, "fig26a.png")
                  let sheetPath = Path.Combine(dir, "sheet.png")
                  let maskPath = Path.Combine(dir, "mask.png")

                  let codeSnap, outSnap, errSnap =
                      runCli [ "snapshot"; fig26a; "-o"; snapPath; "--size"; "256"; "--no-grid" ]

                  Expect.equal codeSnap 0 $"snapshot exit 0 (stderr: %s{errSnap})"
                  Expect.equal (splitNonEmptyLines outSnap) [| $"wrote %s{snapPath}" |] "snapshot stdout"
                  let decSnap = PngTests.decodePng (File.ReadAllBytes snapPath)
                  Expect.equal (decSnap.Width, decSnap.Height, decSnap.ColorType) (256, 256, 2uy) "snapshot PNG metadata"
                  Expect.isGreaterThan (countNonBackgroundRgb decSnap) 100 "snapshot has foreground tree pixels"

                  let codeSheet, outSheet, errSheet =
                      runCli [ "contact-sheet"; fig26a; "-o"; sheetPath; "--size"; "256" ]

                  Expect.equal codeSheet 0 $"contact-sheet exit 0 (stderr: %s{errSheet})"
                  Expect.equal (splitNonEmptyLines outSheet) [| $"wrote %s{sheetPath}" |] "sheet stdout"
                  let decSheet = PngTests.decodePng (File.ReadAllBytes sheetPath)
                  Expect.equal (decSheet.Width, decSheet.Height, decSheet.ColorType) (256, 256, 2uy) "sheet PNG metadata"

                  let codeMask, outMask, errMask =
                      runCli [ "silhouette"; fig26a; "-o"; maskPath; "--size"; "256" ]

                  Expect.equal codeMask 0 $"silhouette exit 0 (stderr: %s{errMask})"
                  Expect.equal (splitNonEmptyLines outMask) [| $"wrote %s{maskPath}" |] "silhouette stdout"
                  let decMask = PngTests.decodePng (File.ReadAllBytes maskPath)
                  Expect.equal (decMask.Width, decMask.Height, decMask.ColorType) (256, 256, 0uy) "mask PNG metadata"

                  // Colored multi-material plant (lilac) renders non-gray colored pixels.
                  let lilacPath = Path.Combine(dir, "lilac.png")

                  let codeLilac, _, errLilac =
                      runCli [ "snapshot"; examplePath "fig3-19-lilac.rp"; "-o"; lilacPath; "--size"; "256"; "--no-grid" ]

                  Expect.equal codeLilac 0 $"lilac exit 0 (stderr: %s{errLilac})"
                  let decLilac = PngTests.decodePng (File.ReadAllBytes lilacPath)

                  let mutable coloredPixels = 0

                  for i = 0 to decLilac.Width * decLilac.Height - 1 do
                      let o = i * 3
                      let r, g, b = decLilac.TopDownPixels[o], decLilac.TopDownPixels[o + 1], decLilac.TopDownPixels[o + 2]

                      if abs (int r - int g) > 15 || abs (int g - int b) > 15 then
                          coloredPixels <- coloredPixels + 1

                  Expect.isGreaterThan coloredPixels 100 "lilac renders distinct palette colors")
          }

          test "32-bit index buffers render models exceeding 65,535 vertices (fig2-8b)" {
              withTempDir (fun dir ->
                  let outPath = Path.Combine(dir, "fig28b.png")

                  let code, _, err =
                      runCli [ "snapshot"; examplePath "fig2-8b.rp"; "-o"; outPath; "--size"; "256"; "--no-grid" ]

                  Expect.equal code 0 $"fig2-8b snapshot exit 0 (stderr: %s{err})"
                  let dec = PngTests.decodePng (File.ReadAllBytes outPath)
                  Expect.isGreaterThan (countNonBackgroundRgb dec) 200 "fig2-8b has foreground pixels")
          }

          test "odd-width readback (301x199) verifies GL_PACK_ALIGNMENT = 1" {
              withTempDir (fun dir ->
                  let fig26a = examplePath "fig2-6a.rp"
                  let snapPath = Path.Combine(dir, "odd-snap.png")
                  let maskPath = Path.Combine(dir, "odd-mask.png")

                  let codeS, _, errS =
                      runCli [ "snapshot"; fig26a; "-o"; snapPath; "--size"; "301x199"; "--no-grid" ]

                  Expect.equal codeS 0 $"odd snapshot exit 0 (stderr: %s{errS})"
                  let decS = PngTests.decodePng (File.ReadAllBytes snapPath)
                  Expect.equal (decS.Width, decS.Height) (301, 199) "exact odd dimensions"
                  Expect.isGreaterThan (countNonBackgroundRgb decS) 100 "odd snapshot has foreground"

                  // Padded fit keeps 1 px outer border cleanly background without row-stride shear.
                  for x = 0 to 300 do
                      Expect.equal (pixelRgb decS x 0) (242uy, 242uy, 240uy) $"top border x=%d{x}"
                      Expect.equal (pixelRgb decS x 198) (242uy, 242uy, 240uy) $"bottom border x=%d{x}"

                  for y = 0 to 198 do
                      Expect.equal (pixelRgb decS 0 y) (242uy, 242uy, 240uy) $"left border y=%d{y}"
                      Expect.equal (pixelRgb decS 300 y) (242uy, 242uy, 240uy) $"right border y=%d{y}"

                  let codeM, _, errM =
                      runCli [ "silhouette"; fig26a; "-o"; maskPath; "--size"; "301x199" ]

                  Expect.equal codeM 0 $"odd silhouette exit 0 (stderr: %s{errM})"
                  let decM = PngTests.decodePng (File.ReadAllBytes maskPath)
                  Expect.equal (decM.Width, decM.Height, decM.ColorType) (301, 199, 0uy) "odd mask metadata"

                  for p in decM.TopDownPixels do
                      Expect.isTrue (p = 0uy || p = 255uy) "strictly binary"

                  for y = 0 to 198 do
                      Expect.equal decM.TopDownPixels[y * 301] 0uy $"mask left border y=%d{y}"
                      Expect.equal decM.TopDownPixels[y * 301 + 300] 0uy $"mask right border y=%d{y}")
          }

          test "viewer loads included .s surfaces and renders them with --surface-div" {
              withTempDir (fun dir ->
                  let pts =
                      [ for i in 0..3 do
                            for j in 0..3 -> $"%g{float j / 3.0 - 0.5} %g{float i / 3.0} 0" ]
                      |> String.concat "\n"

                  File.WriteAllText(
                      Path.Combine(dir, "leaf.s"),
                      $"0 1 0 1 0 0\nPRECISION S: 2 T: 2\nCONTACT POINT: 0 0 0\nEND POINT: 0 1 0\nHEADING: 0 1 0\nUP: 0 0 1\nSIZE: 1\np0\n%s{pts}\n"
                  )

                  let rpPath = Path.Combine(dir, "leaf.rp")
                  File.WriteAllText(rpPath, "include L = \"leaf.s\";\naxiom: '(1) ~L;\n")
                  let snapPath = Path.Combine(dir, "leaf.png")

                  let code, _, err =
                      runCli [ "snapshot"; rpPath; "-o"; snapPath; "--size"; "128"; "--no-grid"; "--surface-div"; "4" ]

                  Expect.equal code 0 $"surface snapshot exit 0 (stderr: %s{err})"
                  let dec = PngTests.decodePng (File.ReadAllBytes snapPath)
                  Expect.isGreaterThan (countNonBackgroundRgb dec) 100 "surface renders foreground pixels")
          }

          test "empty and degenerate-only models render pure background" {
              withTempDir (fun dir ->
                  let degenPath = Path.Combine(dir, "degen.rp")
                  File.WriteAllText(degenPath, "settings { width = 0; }\naxiom: F;\n")

                  for label, input in [ "lsystem-1-7", examplePath "lsystem-1-7.rp"; "degenerate", degenPath ] do
                      let snapPath = Path.Combine(dir, $"%s{label}-snap.png")
                      let maskPath = Path.Combine(dir, $"%s{label}-mask.png")

                      let codeS, _, errS =
                          runCli [ "snapshot"; input; "-o"; snapPath; "--size"; "128"; "--no-grid" ]

                      Expect.equal codeS 0 $"%s{label} snapshot exit 0 (stderr: %s{errS})"
                      let decS = PngTests.decodePng (File.ReadAllBytes snapPath)
                      Expect.equal (countNonBackgroundRgb decS) 0 $"%s{label} snapshot is pure background"

                      let codeM, _, errM = runCli [ "silhouette"; input; "-o"; maskPath; "--size"; "128" ]
                      Expect.equal codeM 0 $"%s{label} silhouette exit 0 (stderr: %s{errM})"
                      let decM = PngTests.decodePng (File.ReadAllBytes maskPath)
                      Expect.isTrue (decM.TopDownPixels |> Array.forall ((=) 0uy)) $"%s{label} silhouette is all zero")
          }

          test "2x2 contact sheet has foreground in every quadrant, 2 px divider, and corner labels" {
              withTempDir (fun dir ->
                  let sheetPath = Path.Combine(dir, "sheet.png")

                  let code, _, err =
                      runCli [ "contact-sheet"; examplePath "fig2-6a.rp"; "-o"; sheetPath; "--size"; "256"; "--no-grid" ]

                  Expect.equal code 0 $"contact-sheet exit 0 (stderr: %s{err})"
                  let dec = PngTests.decodePng (File.ReadAllBytes sheetPath)
                  Expect.equal (dec.Width, dec.Height, dec.ColorType) (256, 256, 2uy) "256x256 RGB24"

                  // 2 px divider crosshair at 127..128
                  for i = 0 to 255 do
                      Expect.equal (pixelRgb dec 127 i) Png.contactSheetDividerRgb "vertical divider left"
                      Expect.equal (pixelRgb dec 128 i) Png.contactSheetDividerRgb "vertical divider right"
                      Expect.equal (pixelRgb dec i 127) Png.contactSheetDividerRgb "horizontal divider top"
                      Expect.equal (pixelRgb dec i 128) Png.contactSheetDividerRgb "horizontal divider bottom"

                  let quadrants = [ "FRONT", 0, 0; "SIDE +90", 128, 0; "3/4 ISO", 0, 128; "TOP", 128, 128 ]

                  for label, qx, qy in quadrants do
                      let mutable labelPixels = 0

                      for y = qy to qy + 24 do
                          for x = qx to qx + 80 do
                              if pixelRgb dec x y = Png.contactSheetLabelTextRgb then
                                  labelPixels <- labelPixels + 1

                      Expect.isGreaterThan labelPixels 10 $"%s{label} has corner label glyphs"

                      // Below the label pill (y >= qy + 24), every quadrant must contain tree pixels.
                      let mutable treePixels = 0

                      for y = qy + 24 to qy + 125 do
                          for x = qx + 2 to qx + 125 do
                              if pixelRgb dec x y <> (242uy, 242uy, 240uy) then
                                  treePixels <- treePixels + 1

                      Expect.isGreaterThan treePixels 50 $"%s{label} quadrant has foreground tree pixels")
          }

          test "silhouette is strictly {0, 255} Gray8 and ignores grid and wireframe toggles" {
              withTempDir (fun dir ->
                  let fig26a = examplePath "fig2-6a.rp"
                  let m1 = Path.Combine(dir, "m1.png")
                  let m2 = Path.Combine(dir, "m2.png")

                  let code, outStr, errStr =
                      runBatch
                          [ $"load \"%s{fig26a}\""
                            "grid on"
                            "wireframe on"
                            $"silhouette \"%s{m1}\" --size 256"
                            "grid off"
                            "wireframe off"
                            $"silhouette \"%s{m2}\" --size 256"
                            "quit" ]

                  Expect.equal code 0 $"batch exit 0 (stderr: %s{errStr})"

                  Expect.equal
                      (splitNonEmptyLines outStr)
                      [| "exit 0"; "exit 0"; "exit 0"; $"wrote %s{m1}"; "exit 0"; "exit 0"; "exit 0"; $"wrote %s{m2}"; "exit 0"; "exit 0" |]
                      "batch output framing"

                  let bytes1 = File.ReadAllBytes m1
                  let bytes2 = File.ReadAllBytes m2
                  Expect.equal bytes1 bytes2 "silhouette ignores grid and wireframe toggles"

                  let dec = PngTests.decodePng bytes1
                  Expect.equal (dec.BitDepth, dec.ColorType) (8uy, 0uy) "8-bit Gray8"
                  Expect.isTrue (dec.TopDownPixels |> Array.forall (fun p -> p = 0uy || p = 255uy)) "strictly binary"
                  Expect.isTrue (dec.TopDownPixels |> Array.exists ((=) 255uy)) "has foreground"
                  Expect.isTrue (dec.TopDownPixels |> Array.exists ((=) 0uy)) "has background")
          }

          test "wireframe and grid toggles visibly alter shaded renders" {
              withTempDir (fun dir ->
                  let fig26a = examplePath "fig2-6a.rp"
                  let plain = Path.Combine(dir, "plain.png")
                  let withGrid = Path.Combine(dir, "grid.png")
                  let withWire = Path.Combine(dir, "wire.png")

                  let code, _, errStr =
                      runBatch
                          [ $"load \"%s{fig26a}\""
                            "grid off"
                            "wireframe off"
                            $"snapshot \"%s{plain}\" --size 256"
                            "grid on"
                            $"snapshot \"%s{withGrid}\" --size 256"
                            "grid off"
                            "wireframe on"
                            $"snapshot \"%s{withWire}\" --size 256"
                            "quit" ]

                  Expect.equal code 0 $"batch exit 0 (stderr: %s{errStr})"
                  let bPlain = File.ReadAllBytes plain
                  let bGrid = File.ReadAllBytes withGrid
                  let bWire = File.ReadAllBytes withWire
                  Expect.notEqual bGrid bPlain "grid changes rendered pixels"
                  Expect.notEqual bWire bPlain "wireframe changes rendered pixels"
                  ignore (PngTests.decodePng bPlain)
                  ignore (PngTests.decodePng bGrid)
                  ignore (PngTests.decodePng bWire))
          }

          test "one-shot --stats, --stats-out, and --pretty match rubberplant stats byte-for-byte" {
              withTempDir (fun dir ->
                  let fig26a = examplePath "fig2-6a.rp"
                  let sheet1 = Path.Combine(dir, "sheet1.png")
                  let stats1 = Path.Combine(dir, "stats1.json")

                  let code1, out1, err1 =
                      runCli [ "contact-sheet"; fig26a; "-o"; sheet1; "--size"; "128"; "--stats"; "--stats-out"; stats1 ]

                  Expect.equal code1 0 $"--stats exit 0 (stderr: %s{err1})"
                  Expect.equal err1 "" "no stderr"
                  Expect.isTrue (out1.EndsWith "\n") "compact JSON ends with LF"
                  Expect.isFalse (out1.Contains "\r") "compact JSON has no CR"
                  Expect.isFalse (out1.Contains "wrote ") "--stats suppresses wrote lines"
                  Expect.equal (File.ReadAllText stats1) out1 "--stats-out matches stdout byte-for-byte"

                  let expectedDigest =
                      File.ReadAllLines(Path.Combine(repoRoot, "tests", "RubberPlant.Backends.Tests", "golden", "stats-digests.txt"))
                      |> Array.pick (fun line ->
                          let parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                          if parts.Length >= 2 && parts[0] = "fig2-6a" then Some parts[1] else None)

                  Expect.equal (sha256Hex out1) expectedDigest "matches golden fig2-6a stats digest"

                  let sheet2 = Path.Combine(dir, "sheet2.png")
                  let stats2 = Path.Combine(dir, "stats2.json")

                  let code2, out2, err2 =
                      runCli [ "contact-sheet"; fig26a; "-o"; sheet2; "--size"; "128"; "--stats-out"; stats2; "--pretty" ]

                  Expect.equal code2 0 $"--stats-out --pretty exit 0 (stderr: %s{err2})"
                  Expect.equal (splitNonEmptyLines out2) [| $"wrote %s{sheet2}"; $"wrote %s{stats2}" |] "wrote lines without --stats"
                  let prettyJson = File.ReadAllText stats2
                  Expect.isTrue (prettyJson.EndsWith "\n") "pretty JSON ends with LF"
                  Expect.isFalse (prettyJson.Contains "\r") "pretty JSON has no CR"
                  Expect.stringContains prettyJson "\n  \"derivation\": {\n" "2-space indented"
                  use doc = JsonDocument.Parse prettyJson
                  Expect.isGreaterThan (doc.RootElement.GetProperty("geometry").GetProperty("segment_count").GetInt32()) 0 "valid JSON")
          }

          test "batch session handles pre-load errors, non-mutating overrides, reload, and RP0206/RP0207 recovery" {
              withTempDir (fun dir ->
                  let fig26a = examplePath "fig2-6a.rp"
                  let explode = Path.Combine(dir, "explode.rp")
                  File.WriteAllText(explode, "settings { derivations = 40; }\naxiom: F;\nF -> F F;\n")

                  let s1 = Path.Combine(dir, "s1.png")
                  let sOver = Path.Combine(dir, "s-over.png")
                  let s2 = Path.Combine(dir, "s2.png")
                  let sheet = Path.Combine(dir, "sheet.png")
                  let s3 = Path.Combine(dir, "s3.png")
                  let sAfter = Path.Combine(dir, "s-after.png")
                  let sReloaded = Path.Combine(dir, "s-reloaded.png")

                  let code, outStr, errStr =
                      runBatch
                          [ $"snapshot \"%s{s1}\"" // pre-load error
                            "fit" // pre-load error
                            "reload" // pre-load error
                            "stats" // pre-load error
                            "camera 90 0"
                            "size 200 150"
                            "grid off"
                            "wireframe off"
                            $"load \"%s{fig26a}\""
                            $"snapshot \"%s{s1}\""
                            $"snapshot \"%s{sOver}\" --camera 0,90,fit --size 128"
                            $"snapshot \"%s{s2}\""
                            $"contact-sheet \"%s{sheet}\" --size 128"
                            $"snapshot \"%s{s3}\""
                            $"load \"%s{explode}\" --max-modules 10" // RP0206 failure
                            $"load \"%s{fig26a}\" --max-primitives 1" // RP0207 failure
                            $"snapshot \"%s{sAfter}\""
                            "stats"
                            "reload --keep-camera"
                            $"snapshot \"%s{sReloaded}\""
                            "fit"
                            "quit" ]

                  Expect.equal code 0 "batch exits 0"
                  Expect.stringContains errStr "RP0206" "RP0206 reported"
                  Expect.stringContains errStr "RP0207" "RP0207 reported"

                  let lines = splitNonEmptyLines outStr

                  let expectedLines =
                      [| "exit 1" // snapshot pre-load
                         "exit 1" // fit pre-load
                         "exit 1" // reload pre-load
                         "exit 1" // stats pre-load
                         "exit 0" // camera 90 0
                         "exit 0" // size 200 150
                         "exit 0" // grid off
                         "exit 0" // wireframe off
                         "exit 0" // load fig2-6a
                         $"wrote %s{s1}"
                         "exit 0"
                         $"wrote %s{sOver}"
                         "exit 0"
                         $"wrote %s{s2}"
                         "exit 0"
                         $"wrote %s{sheet}"
                         "exit 0"
                         $"wrote %s{s3}"
                         "exit 0"
                         "exit 1" // RP0206
                         "exit 1" // RP0207
                         $"wrote %s{sAfter}"
                         "exit 0"
                         lines[23] // stats JSON line verified below
                         "exit 0"
                         "exit 0" // reload --keep-camera
                         $"wrote %s{sReloaded}"
                         "exit 0"
                         "exit 0" // fit
                         "exit 0" |] // quit

                  Expect.equal lines expectedLines "exact batch command output and exit framing"

                  let expectedDigest =
                      File.ReadAllLines(Path.Combine(repoRoot, "tests", "RubberPlant.Backends.Tests", "golden", "stats-digests.txt"))
                      |> Array.pick (fun line ->
                          let parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                          if parts.Length >= 2 && parts[0] = "fig2-6a" then Some parts[1] else None)

                  Expect.equal (sha256Hex (lines[23] + "\n")) expectedDigest "recovered stats matches fig2-6a golden"

                  let decOver = PngTests.decodePng (File.ReadAllBytes sOver)
                  Expect.equal (decOver.Width, decOver.Height) (128, 128) "override size"

                  let b1 = File.ReadAllBytes s1
                  Expect.equal (File.ReadAllBytes s2) b1 "snapshot override did not mutate session"
                  Expect.equal (File.ReadAllBytes s3) b1 "contact-sheet did not mutate session"
                  Expect.equal (File.ReadAllBytes sAfter) b1 "failed loads kept GPU scene and camera intact"
                  Expect.equal (File.ReadAllBytes sReloaded) b1 "reload --keep-camera preserved scene and camera")
          }

          test "atomic write failures exit 1, preserve existing targets, and leave no temporary files" {
              withTempDir (fun dir ->
                  let fig26a = examplePath "fig2-6a.rp"
                  let existingDir = Path.Combine(dir, "subdir")
                  Directory.CreateDirectory existingDir |> ignore

                  let codeDir, _, _ =
                      runCli [ "snapshot"; fig26a; "-o"; existingDir; "--size"; "64" ]

                  Expect.equal codeDir 1 "directory target fails"

                  let missingParent = Path.Combine(dir, "missing", "out.png")

                  let codeMissing, _, _ =
                      runCli [ "snapshot"; fig26a; "-o"; missingParent; "--size"; "64" ]

                  Expect.equal codeMissing 1 "missing parent dir fails"

                  let collidePath = Path.Combine(dir, "same.png")

                  let codeCollide, _, _ =
                      runCli [ "snapshot"; fig26a; "-o"; collidePath; "--stats-out"; collidePath; "--size"; "64" ]

                  Expect.equal codeCollide 1 "colliding output and --stats-out fails"

                  // Read-only --stats-out target fails precheck and preserves pre-existing PNG untouched.
                  let existingPng = Path.Combine(dir, "existing.png")
                  let roStats = Path.Combine(dir, "ro-stats.json")
                  let sentinel = [| 1uy; 2uy; 3uy; 4uy |]
                  File.WriteAllBytes(existingPng, sentinel)
                  File.WriteAllText(roStats, "keep")
                  File.SetAttributes(roStats, FileAttributes.ReadOnly)

                  let codeRo, outRo, errRo =
                      runCli [ "snapshot"; fig26a; "-o"; existingPng; "--stats-out"; roStats; "--size"; "64" ]

                  Expect.equal codeRo 1 $"read-only --stats-out fails (stderr: %s{errRo})"
                  Expect.equal outRo "" "nothing written to stdout on write failure"
                  Expect.equal (File.ReadAllBytes existingPng) sentinel "existing PNG preserved on stats-out failure"
                  Expect.equal (File.ReadAllText roStats) "keep" "read-only stats file untouched"

                  let temps = Directory.GetFiles(dir, ".rubberplant-viewer.*.tmp", SearchOption.AllDirectories)
                  Expect.isEmpty temps "no temporary files left behind")
          }

          test "CLI exit codes, batch render --stats, and IOffscreenRenderer error paths behave cleanly" {
              let codeEmpty, outEmpty, _ = runCli []
              Expect.equal codeEmpty 2 "empty argv exits 2"
              Expect.stringContains outEmpty "Usage:" "empty argv prints usage"

              let codeHelp, outHelp, _ = runCli [ "--help" ]
              Expect.equal codeHelp 0 "--help exits 0"
              Expect.stringContains outHelp "Usage:" "--help prints usage"

              let codeBad, _, errBad = runCli [ "snaphsot"; "a.rp" ]
              Expect.equal codeBad 2 "unknown command exits 2"
              Expect.stringContains errBad "unknown command" "unknown command message"

              let codeMissing, _, errMissing = runCli [ "view"; "does-not-exist-pr4.rp" ]
              Expect.equal codeMissing 1 "view with missing file fails before opening window"
              Expect.isNonEmpty errMissing "missing file diagnostic emitted"

              withTempDir (fun dir ->
                  let fig26a = examplePath "fig2-6a.rp"
                  let snapPath = Path.Combine(dir, "batch-stats.png")
                  let statsPath = Path.Combine(dir, "batch-stats.json")

                  let code, outStr, errStr =
                      runBatch
                          [ $"load \"%s{fig26a}\""
                            $"snapshot \"%s{snapPath}\" --size 64 --stats --stats-out \"%s{statsPath}\""
                            "quit" ]

                  Expect.equal code 0 $"batch --stats exits 0 (stderr: %s{errStr})"
                  let lines = splitNonEmptyLines outStr
                  Expect.equal lines.Length 4 "load exit, stats json, snap exit, quit exit"
                  Expect.equal (lines[1] + "\n") (File.ReadAllText statsPath) "batch --stats matches --stats-out"
                  Expect.isFalse (outStr.Contains "wrote ") "batch --stats suppresses wrote lines")

              let r = Expect.wantOk (Renderer.createHeadless ()) "createHeadless"
              let cam = Camera.fit Camera.emptyBounds 35.0 25.0 1.0
              Expect.isError (r.RenderShaded 0 64 cam false false) "width 0 rejected"
              Expect.isError (r.RenderSilhouette 64 0 cam) "height 0 rejected"
              r.Dispose()
              r.Dispose() // idempotent
              Expect.isError (r.RenderShaded 64 64 cam false false) "RenderShaded after Dispose returns Error"
              Expect.isError (r.RenderSilhouette 64 64 cam) "RenderSilhouette after Dispose returns Error"
          }

          test "interactive view with streamed stdin wakes WaitEvents via PostWakeup and executes all commands" {
              withTempDir (fun dir ->
                  let fig26a = examplePath "fig2-6a.rp"
                  let snapPath = Path.Combine(dir, "view-snap.png")
                  let maskPath = Path.Combine(dir, "view-mask.png")

                  let viewArgs =
                      match Expect.wantOk (Protocol.parseCli [ "view"; fig26a; "--size"; "320x240" ]) "parse view" with
                      | Protocol.View v -> v
                      | other -> failwith $"expected View, got %A{other}"

                  use incoming = new BlockingCollection<string>()
                  use exitSem = new SemaphoreSlim(0)

                  use stdout =
                      new SignallingTextWriter(fun line ->
                          if line.StartsWith("exit ", StringComparison.Ordinal) then
                              exitSem.Release() |> ignore)

                  use stdin = new AsyncQueueTextReader(incoming)
                  use stderr = new StringWriter()

                  let mutable timedOutCmd: string option = None

                  let sendAndWait (cmd: string) =
                      if timedOutCmd.IsNone then
                          incoming.Add(cmd)

                          if not (exitSem.Wait(15000)) then
                              timedOutCmd <- Some cmd

                  let producer =
                      Thread(
                          ThreadStart(fun () ->
                              sendAndWait "camera 90 0"
                              sendAndWait "wireframe on"
                              sendAndWait "grid off"
                              sendAndWait $"snapshot \"%s{snapPath}\" --size 200"
                              sendAndWait $"silhouette \"%s{maskPath}\" --size 128"
                              sendAndWait "stats"
                              sendAndWait $"load \"%s{fig26a}\" --max-modules 5"
                              sendAndWait "reload --keep-camera"
                              sendAndWait "quit"
                              incoming.CompleteAdding())
                      )

                  producer.IsBackground <- true
                  producer.Start()

                  let exitCode = Program.executeViewWithVisibility false viewArgs stdin stdout stderr
                  producer.Join(5000) |> ignore

                  Expect.isNone timedOutCmd $"timed out waiting for exit line after %A{timedOutCmd}"
                  Expect.equal exitCode 0 $"view exits 0 (stderr: %s{stderr.ToString()})"
                  Expect.stringContains (stderr.ToString()) "RP0206" "budget error reported"

                  let lines = splitNonEmptyLines (stdout.AllText())
                  Expect.equal lines.Length 12 $"expected 12 stdout lines, got %d{lines.Length}: %A{lines}"
                  Expect.equal lines[0] "exit 0" "camera 90 0"
                  Expect.equal lines[1] "exit 0" "wireframe on"
                  Expect.equal lines[2] "exit 0" "grid off"
                  Expect.equal lines[3] $"wrote %s{snapPath}" "snapshot wrote"
                  Expect.equal lines[4] "exit 0" "snapshot exit"
                  Expect.equal lines[5] $"wrote %s{maskPath}" "silhouette wrote"
                  Expect.equal lines[6] "exit 0" "silhouette exit"
                  Expect.equal lines[8] "exit 0" "stats exit"
                  Expect.equal lines[9] "exit 1" "RP0206 failed load exit"
                  Expect.equal lines[10] "exit 0" "reload --keep-camera exit"
                  Expect.equal lines[11] "exit 0" "quit exit"

                  let expectedDigest =
                      File.ReadAllLines(Path.Combine(repoRoot, "tests", "RubberPlant.Backends.Tests", "golden", "stats-digests.txt"))
                      |> Array.pick (fun line ->
                          let parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                          if parts.Length >= 2 && parts[0] = "fig2-6a" then Some parts[1] else None)

                  Expect.equal (sha256Hex (lines[7] + "\n")) expectedDigest "view stats matches golden"

                  let decSnap = PngTests.decodePng (File.ReadAllBytes snapPath)
                  Expect.equal (decSnap.Width, decSnap.Height, decSnap.ColorType) (200, 200, 2uy) "snap PNG"
                  Expect.isGreaterThan (countNonBackgroundRgb decSnap) 100 "snap has foreground"

                  let decMask = PngTests.decodePng (File.ReadAllBytes maskPath)
                  Expect.equal (decMask.Width, decMask.Height, decMask.ColorType) (128, 128, 0uy) "mask PNG"
                  Expect.isTrue (decMask.TopDownPixels |> Array.forall (fun p -> p = 0uy || p = 255uy)) "strictly binary")
          }

          test "bare view without initial file handles pre-load stats, live load, and contact-sheet" {
              withTempDir (fun dir ->
                  let fig26a = examplePath "fig2-6a.rp"
                  let sheetPath = Path.Combine(dir, "bare-sheet.png")

                  let viewArgs =
                      match Expect.wantOk (Protocol.parseCli [ "view"; "--camera"; "90,0"; "--no-grid"; "--size"; "256" ]) "parse bare view" with
                      | Protocol.View v -> v
                      | other -> failwith $"expected View, got %A{other}"

                  let cmds =
                      [ "stats"
                        $"load \"%s{fig26a}\""
                        $"contact-sheet \"%s{sheetPath}\" --size 128"
                        "quit" ]

                  use stdin = new StringReader(String.concat "\n" cmds + "\n")
                  use stdout = new StringWriter()
                  use stderr = new StringWriter()

                  let exitCode = Program.executeViewWithVisibility false viewArgs stdin stdout stderr
                  Expect.equal exitCode 0 "bare view exits 0"
                  Expect.stringContains (stderr.ToString()) "no model loaded" "pre-load stats diagnostic"

                  let lines = splitNonEmptyLines (stdout.ToString())

                  Expect.equal
                      lines
                      [| "exit 1"
                         "exit 0"
                         $"wrote %s{sheetPath}"
                         "exit 0"
                         "exit 0" |]
                      "bare view stdout framing"

                  let decSheet = PngTests.decodePng (File.ReadAllBytes sheetPath)
                  Expect.equal (decSheet.Width, decSheet.Height, decSheet.ColorType) (128, 128, 2uy) "sheet PNG")
          }

          test "IInteractiveRenderer restores default framebuffer after offscreen renders and safely ignores post-dispose wakeups" {
              let opts: Renderer.WindowOptions =
                  { Width = 240
                    Height = 180
                    Title = "RubberPlant — test"
                    Visible = false }

              let r = Expect.wantOk (Renderer.createWindow opts) "createWindow"

              let callbackFields =
                  r.GetType().GetFields(
                      System.Reflection.BindingFlags.Instance
                      ||| System.Reflection.BindingFlags.NonPublic
                      ||| System.Reflection.BindingFlags.Public
                  )
                  |> Array.filter (fun f -> f.FieldType = typeof<Delegate[]>)

              Expect.isNonEmpty callbackFields "GlInteractiveRenderer roots callback delegates in an instance field"

              r.UploadScene Scene.emptyScene (Scene.buildGrid Scene.emptyScene)
              r.SetTitle("RubberPlant — updated")
              let cam = Camera.fit Camera.emptyBounds 35.0 25.0 1.0
              r.RenderToWindow cam true false
              GC.Collect()
              GC.WaitForPendingFinalizers()
              GC.Collect()
              let offscreen = Expect.wantOk (r.RenderShaded 96 64 cam true false) "offscreen shaded inside window"
              Expect.equal offscreen.Length (96 * 64 * 3) "offscreen buffer size"
              r.RenderToWindow cam true true
              r.Dispose()
              r.Dispose()
              r.PostWakeup()
          }

          test "interactive view --watch auto-reloads in-place edits and atomic renames while preserving valid scene on syntax errors" {
              withTempDir (fun dir ->
                  let watched = Path.Combine(dir, "watched.rp")
                  File.WriteAllText(watched, "axiom: F;\n")

                  let viewArgs =
                      match Expect.wantOk (Protocol.parseCli [ "view"; watched; "--watch"; "--size"; "240x180" ]) "parse view --watch" with
                      | Protocol.View v -> v
                      | other -> failwith $"expected View, got %A{other}"

                  use incoming = new BlockingCollection<string>()
                  use lineSem = new SemaphoreSlim(0)
                  let outLines = ConcurrentQueue<string>()

                  use stdout =
                      new SignallingTextWriter(fun line ->
                          outLines.Enqueue(line)
                          lineSem.Release() |> ignore)

                  use stdin = new AsyncQueueTextReader(incoming)
                  use errSem = new SemaphoreSlim(0)

                  use stderr =
                      new SignallingTextWriter(fun line ->
                          if line.Contains("error", StringComparison.OrdinalIgnoreCase) then
                              errSem.Release() |> ignore)

                  let drainNextLine (timeoutMs: int) =
                      if lineSem.Wait(timeoutMs) then
                          let mutable line = ""
                          if outLines.TryDequeue(&line) then Some line else None
                      else
                          None

                  let querySegmentCount () =
                      incoming.Add("stats")

                      match drainNextLine 15000, drainNextLine 15000 with
                      | Some jsonLine, Some "exit 0" ->
                          use doc = JsonDocument.Parse(jsonLine)
                          doc.RootElement.GetProperty("geometry").GetProperty("segment_count").GetInt32()
                      | a, b -> failwith $"unexpected stats response: %A{a}, %A{b}"

                  let waitForSegmentCount (expected: int) =
                      let deadline = DateTime.UtcNow.AddSeconds(5.0)
                      let mutable current = querySegmentCount ()

                      while current <> expected && DateTime.UtcNow < deadline do
                          Thread.Sleep(40)
                          current <- querySegmentCount ()

                      Expect.equal current expected $"watched model auto-reloaded to segment_count=%d{expected}"

                  let mutable failure: exn option = None

                  let driver =
                      Thread(
                          ThreadStart(fun () ->
                              try
                                  Expect.equal (querySegmentCount ()) 1 "initial segment_count = 1"

                                  // 1. In-place edit -> auto-reloads to 3 segments.
                                  File.WriteAllText(watched, "axiom: F F F;\n")
                                  waitForSegmentCount 3

                                  // 2. Atomic temp-file rename -> auto-reloads to 5 segments.
                                  let tmp = Path.Combine(dir, "watched.tmp")
                                  File.WriteAllText(tmp, "axiom: F F F F F;\n")
                                  File.Move(tmp, watched, true)
                                  waitForSegmentCount 5

                                  // 3. Broken syntax -> preserves 5 segments and emits diagnostic to stderr.
                                  File.WriteAllText(watched, "axiom: ;\n")
                                  Expect.isTrue (errSem.Wait(5000)) "broken syntax emitted error to stderr"
                                  Expect.equal (querySegmentCount ()) 5 "syntax error preserves last valid scene"

                                  incoming.Add("quit")
                                  drainNextLine 15000 |> ignore
                                  incoming.CompleteAdding()
                              with ex ->
                                  failure <- Some ex
                                  incoming.Add("quit")
                                  incoming.CompleteAdding())
                      )

                  driver.IsBackground <- true
                  driver.Start()

                  let exitCode = Program.executeViewWithVisibility false viewArgs stdin stdout stderr
                  driver.Join(5000) |> ignore

                  match failure with
                  | Some ex -> raise ex
                  | None -> ()

                  Expect.equal exitCode 0 $"view --watch exits 0 (stderr: %s{stderr.AllText()})"
                  Expect.isNonEmpty (stderr.AllText()) "broken syntax emitted error to stderr")
          } ]
