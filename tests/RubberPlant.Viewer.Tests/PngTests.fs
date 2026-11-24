module RubberPlant.Viewer.Tests.PngTests

open System
open System.IO
open System.IO.Compression
open Expecto
open RubberPlant.Viewer.Png

type private DecodedPng =
    { Width: int
      Height: int
      BitDepth: byte
      ColorType: byte
      /// Top-down tightly packed pixel bytes.
      TopDownPixels: byte[] }

let private readU32BE (bytes: byte[]) (offset: int) : uint32 =
    (uint32 bytes[offset] <<< 24)
    ||| (uint32 bytes[offset + 1] <<< 16)
    ||| (uint32 bytes[offset + 2] <<< 8)
    ||| uint32 bytes[offset + 3]

/// Pure-F# PNG decoder verifying signature, IHDR, every chunk CRC-32, IDAT zlib
/// decompression, filter bytes, and IEND termination.
let private decodePng (bytes: byte[]) : DecodedPng =
    let sigBytes = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |]
    Expect.isGreaterThanOrEqual bytes.Length 8 "has signature"
    Expect.equal bytes[0..7] sigBytes "PNG signature exact"

    let mutable pos = 8
    let mutable width = 0
    let mutable height = 0
    let mutable bitDepth = 0uy
    let mutable colorType = 0uy
    let mutable sawIhdr = false
    let mutable sawIend = false
    use idatStream = new MemoryStream()

    while pos < bytes.Length do
        Expect.isFalse sawIend "no trailing bytes after IEND"
        let len = int (readU32BE bytes pos)
        let chunkType = Text.Encoding.ASCII.GetString(bytes, pos + 4, 4)
        let dataStart = pos + 8
        let crcStart = dataStart + len
        let storedCrc = readU32BE bytes crcStart
        let computedCrc = crc32 bytes[pos + 4 .. crcStart - 1]
        Expect.equal storedCrc computedCrc $"CRC-32 valid for chunk %s{chunkType}"

        match chunkType with
        | "IHDR" ->
            Expect.isFalse sawIhdr "single IHDR"
            Expect.equal len 13 "IHDR length 13"
            width <- int (readU32BE bytes dataStart)
            height <- int (readU32BE bytes (dataStart + 4))
            bitDepth <- bytes[dataStart + 8]
            colorType <- bytes[dataStart + 9]
            Expect.equal bytes[dataStart + 10] 0uy "compression 0"
            Expect.equal bytes[dataStart + 11] 0uy "filter method 0"
            Expect.equal bytes[dataStart + 12] 0uy "interlace 0"
            sawIhdr <- true
        | "IDAT" ->
            Expect.isTrue sawIhdr "IDAT after IHDR"
            idatStream.Write(bytes, dataStart, len)
        | "IEND" ->
            Expect.equal len 0 "IEND empty"
            sawIend <- true
        | other -> failwith $"unexpected chunk %s{other}"

        pos <- crcStart + 4

    Expect.isTrue sawIhdr "saw IHDR"
    Expect.isTrue sawIend "saw IEND"

    let bpp =
        match colorType with
        | 0uy -> 1
        | 2uy -> 3
        | c -> failwith $"unexpected colorType %d{int c}"

    idatStream.Position <- 0L
    use zlib = new ZLibStream(idatStream, CompressionMode.Decompress)
    use raw = new MemoryStream()
    zlib.CopyTo(raw)
    let rawBytes = raw.ToArray()
    let stride = width * bpp
    Expect.equal rawBytes.Length (height * (stride + 1)) "decompressed scanline byte length"

    let pixels = Array.zeroCreate<byte> (height * stride)

    for y = 0 to height - 1 do
        let rowStart = y * (stride + 1)
        Expect.equal rawBytes[rowStart] 0uy $"row %d{y} filter byte is 0"
        Buffer.BlockCopy(rawBytes, rowStart + 1, pixels, y * stride, stride)

    { Width = width
      Height = height
      BitDepth = bitDepth
      ColorType = colorType
      TopDownPixels = pixels }

let private makeSolidTile (tileSize: int) (r: byte, g: byte, b: byte) =
    let buf = Array.zeroCreate<byte> (tileSize * tileSize * 3)

    for i = 0 to tileSize * tileSize - 1 do
        buf[i * 3] <- r
        buf[i * 3 + 1] <- g
        buf[i * 3 + 2] <- b

    buf

[<Tests>]
let tests =
    testList
        "Png"
        [ test "crc32 matches independent IEEE 802.3 and PNG IEND test vectors" {
              Expect.equal (crc32 (Text.Encoding.ASCII.GetBytes "123456789")) 0xCBF43926u "IEEE 802.3 standard check value"
              Expect.equal (crc32 (Text.Encoding.ASCII.GetBytes "IEND")) 0xAE426082u "PNG IEND chunk CRC"
          }

          test "invalid dimensions or buffer lengths throw ArgumentException" {
              Expect.throwsT<ArgumentException> (fun () -> encodeRgb24 0 4 [||] |> ignore) "width 0"
              Expect.throwsT<ArgumentException> (fun () -> encodeGray8 4 0 [||] |> ignore) "height 0"
              Expect.throwsT<ArgumentException> (fun () -> encodeRgb24 2 2 (Array.zeroCreate 11) |> ignore) "short rgb buffer"
              Expect.throwsT<ArgumentException> (fun () -> encodeGray8 2 2 (Array.zeroCreate 5) |> ignore) "long gray buffer"

              let smallTile = Array.zeroCreate<byte> (32 * 32 * 3)
              Expect.throwsT<ArgumentException>
                  (fun () -> composeContactSheet 32 smallTile smallTile smallTile smallTile |> ignore)
                  "tileSize < 64"

              let okTile = Array.zeroCreate<byte> (64 * 64 * 3)
              Expect.throwsT<ArgumentException>
                  (fun () -> composeContactSheet 64 okTile okTile okTile smallTile |> ignore)
                  "mismatched tile length"
          }

          test "RGB24 round-trip verifies vertical flip, CRC-32, odd widths, and exact pixels" {
              for w, h in [ 1, 1; 3, 2; 7, 13; 301, 5 ] do
                  let bottomUp = Array.zeroCreate<byte> (w * h * 3)

                  for y = 0 to h - 1 do
                      for x = 0 to w - 1 do
                          let o = (y * w + x) * 3
                          bottomUp[o] <- byte ((x * 7 + y * 13) % 256)
                          bottomUp[o + 1] <- byte ((x * 3 + y * 29) % 256)
                          bottomUp[o + 2] <- byte ((x * 11 + y * 5) % 256)

                  let png = encodeRgb24 w h bottomUp
                  let dec = decodePng png
                  Expect.equal dec.Width w $"width %d{w}"
                  Expect.equal dec.Height h $"height %d{h}"
                  Expect.equal dec.BitDepth 8uy "bitDepth 8"
                  Expect.equal dec.ColorType 2uy "colorType RGB24"

                  // Top-down row y must match bottom-up row (h - 1 - y).
                  for yTop = 0 to h - 1 do
                      let yBot = h - 1 - yTop

                      for x = 0 to w - 1 do
                          let oTop = (yTop * w + x) * 3
                          let oBot = (yBot * w + x) * 3
                          Expect.equal dec.TopDownPixels[oTop] bottomUp[oBot] $"R ({x},{yTop})"
                          Expect.equal dec.TopDownPixels[oTop + 1] bottomUp[oBot + 1] $"G ({x},{yTop})"
                          Expect.equal dec.TopDownPixels[oTop + 2] bottomUp[oBot + 2] $"B ({x},{yTop})"
          }

          test "Gray8 round-trip preserves strictly binary {0, 255} mask and odd widths" {
              for w, h in [ 1, 1; 5, 7; 301, 9 ] do
                  let bottomUp =
                      Array.init (w * h) (fun i ->
                          let x = i % w
                          let y = i / w
                          if (x + y) % 2 = 0 then 255uy else 0uy)

                  let png = encodeGray8 w h bottomUp
                  let dec = decodePng png
                  Expect.equal dec.Width w "width"
                  Expect.equal dec.Height h "height"
                  Expect.equal dec.BitDepth 8uy "bitDepth 8"
                  Expect.equal dec.ColorType 0uy "colorType Gray8"

                  for yTop = 0 to h - 1 do
                      let yBot = h - 1 - yTop

                      for x = 0 to w - 1 do
                          let p = dec.TopDownPixels[yTop * w + x]
                          Expect.equal p bottomUp[yBot * w + x] $"gray ({x},{yTop})"
                          Expect.isTrue (p = 0uy || p = 255uy) "strictly binary"
          }

          test "2x2 contact sheet places quadrants, 2 px divider crosshair, and tile-clipped corner labels" {
              for tileSize in [ 64; 512 ] do
                  let sheetSize = tileSize * 2
                  // Distinct solid tile colors far from divider (#cccccc) and label text (#282828).
                  let cTL = (10uy, 80uy, 160uy)
                  let cTR = (160uy, 60uy, 20uy)
                  let cBL = (20uy, 140uy, 60uy)
                  let cBR = (120uy, 40uy, 140uy)

                  let png =
                      encodeContactSheet
                          tileSize
                          (makeSolidTile tileSize cTL)
                          (makeSolidTile tileSize cTR)
                          (makeSolidTile tileSize cBL)
                          (makeSolidTile tileSize cBR)

                  let dec = decodePng png
                  Expect.equal dec.Width sheetSize $"sheet width %d{sheetSize}"
                  Expect.equal dec.Height sheetSize $"sheet height %d{sheetSize}"
                  Expect.equal dec.ColorType 2uy "RGB24"

                  let pixelAt x yTop =
                      let o = (yTop * sheetSize + x) * 3
                      dec.TopDownPixels[o], dec.TopDownPixels[o + 1], dec.TopDownPixels[o + 2]

                  // Bottom-right corner of each quadrant is untouched by the top-left label and divider.
                  Expect.equal (pixelAt (tileSize - 3) (tileSize - 3)) cTL $"tileSize=%d{tileSize} TL quadrant"
                  Expect.equal (pixelAt (sheetSize - 3) (tileSize - 3)) cTR $"tileSize=%d{tileSize} TR quadrant"
                  Expect.equal (pixelAt (tileSize - 3) (sheetSize - 3)) cBL $"tileSize=%d{tileSize} BL quadrant"
                  Expect.equal (pixelAt (sheetSize - 3) (sheetSize - 3)) cBR $"tileSize=%d{tileSize} BR quadrant"

                  // 2 px divider crosshair at tileSize - 1 and tileSize along both axes.
                  for i = 0 to sheetSize - 1 do
                      Expect.equal (pixelAt (tileSize - 1) i) contactSheetDividerRgb "vertical divider left px"
                      Expect.equal (pixelAt tileSize i) contactSheetDividerRgb "vertical divider right px"
                      Expect.equal (pixelAt i (tileSize - 1)) contactSheetDividerRgb "horizontal divider top px"
                      Expect.equal (pixelAt i tileSize) contactSheetDividerRgb "horizontal divider bottom px"

                  // Each quadrant has corner label text pixels inside its top-left corner and none bleeding across divider.
                  let quadrants =
                      [ "FRONT", 0, 0, cTL
                        "SIDE +90", tileSize, 0, cTR
                        "3/4 ISO", 0, tileSize, cBL
                        "TOP", tileSize, tileSize, cBR ]

                  for name, qx, qy, baseColor in quadrants do
                      let mutable textCount = 0

                      for y = qy to qy + tileSize - 2 do
                          for x = qx to qx + tileSize - 2 do
                              if pixelAt x y = contactSheetLabelTextRgb then
                                  textCount <- textCount + 1

                      Expect.isGreaterThan textCount 10 $"tileSize=%d{tileSize} %s{name} has rendered label glyphs"

                      // Row immediately below the label area (bottom half of quadrant) remains pure baseColor.
                      Expect.equal (pixelAt (qx + 4) (qy + tileSize - 4)) baseColor $"%s{name} bottom-left untouched by label"
          } ]
