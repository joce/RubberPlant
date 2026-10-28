module RubberPlant.Backends.Tests.PaletteTests

open Expecto
open RubberPlant.Backends

[<Tests>]
let tests =
    testList
        "Palette"
        [ test "8 colors" { Expect.equal Palette.colors.Length 8 "" }

          test "entries wrap around, negative indices too" {
              Expect.equal (Palette.entry 0) 0 ""
              Expect.equal (Palette.entry 7) 7 ""
              Expect.equal (Palette.entry 8) 0 ""
              Expect.equal (Palette.entry 9) 1 ""
              Expect.equal (Palette.entry -1) 7 ""
              Expect.equal (Palette.entry -8) 0 ""
              Expect.equal (Palette.entry System.Int32.MinValue) 0 ""
              Expect.equal (Palette.entry System.Int32.MaxValue) 7 ""
          }

          test "hex is the CSS form of rgb" {
              Expect.equal (Palette.hex 0) "#1a1a1a" ""
              Expect.equal (Palette.hex 1) "#2e7d32" ""
              Expect.equal (Palette.hex 7) "#ef6c00" ""
              Expect.equal (Palette.hex 15) "#ef6c00" ""
              Expect.equal (Palette.rgb 1) (0x2euy, 0x7duy, 0x32uy) ""
          } ]
