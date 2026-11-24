/// Pure PNG encoder (RGB24 and Gray8) and 2x2 contact-sheet compositor with
/// embedded 5x7 bitmap labels (docs/viewer.md §5.5–§5.6).
module RubberPlant.Viewer.Png

open System
open System.IO
open System.IO.Compression

let private pngSignature = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |]
let private chunkIhdr = [| byte 'I'; byte 'H'; byte 'D'; byte 'R' |]
let private chunkIdat = [| byte 'I'; byte 'D'; byte 'A'; byte 'T' |]
let private chunkIend = [| byte 'I'; byte 'E'; byte 'N'; byte 'D' |]

/// Precomputed IEEE 802.3 CRC-32 lookup table (polynomial 0xEDB88320).
let private crcTable =
    Array.init 256 (fun n ->
        let mutable c = uint32 n

        for _ = 0 to 7 do
            if (c &&& 1u) <> 0u then
                c <- 0xEDB88320u ^^^ (c >>> 1)
            else
                c <- c >>> 1

        c)

let private crcUpdate (crc: uint32) (bytes: byte[]) (offset: int) (length: int) : uint32 =
    let mutable c = crc

    for i = offset to offset + length - 1 do
        c <- crcTable[int ((c ^^^ uint32 bytes[i]) &&& 0xFFu)] ^^^ (c >>> 8)

    c

/// Computes standard PNG CRC-32 over a byte array.
let crc32 (data: byte[]) : uint32 =
    (crcUpdate 0xFFFFFFFFu data 0 data.Length) ^^^ 0xFFFFFFFFu

let private writeU32BE (s: Stream) (v: uint32) =
    s.WriteByte(byte (v >>> 24))
    s.WriteByte(byte (v >>> 16))
    s.WriteByte(byte (v >>> 8))
    s.WriteByte(byte v)

let private writeChunk (s: Stream) (chunkType: byte[]) (data: byte[]) =
    writeU32BE s (uint32 data.Length)
    s.Write(chunkType, 0, 4)

    if data.Length > 0 then
        s.Write(data, 0, data.Length)

    let crc =
        let c1 = crcUpdate 0xFFFFFFFFu chunkType 0 4
        let c2 = crcUpdate c1 data 0 data.Length
        c2 ^^^ 0xFFFFFFFFu

    writeU32BE s crc

let private encode (width: int) (height: int) (colorType: byte) (bytesPerPixel: int) (bottomUp: byte[]) : byte[] =
    if width <= 0 || height <= 0 then
        invalidArg "width" "PNG dimensions must be positive"

    let expected = int64 width * int64 height * int64 bytesPerPixel

    if int64 bottomUp.Length <> expected then
        invalidArg "bottomUp" "pixel buffer length does not match PNG dimensions"

    let stride = width * bytesPerPixel

    use idatBuf = new MemoryStream()

    do
        use zlib = new ZLibStream(idatBuf, CompressionLevel.Fastest, leaveOpen = true)

        // Flip OpenGL's bottom-up rows to top-down PNG scanlines while streaming.
        for y = height - 1 downto 0 do
            zlib.WriteByte 0uy // filter type 0 (None)
            zlib.Write(bottomUp, y * stride, stride)

    let ihdr = Array.zeroCreate<byte> 13
    ihdr[0] <- byte (uint32 width >>> 24)
    ihdr[1] <- byte (uint32 width >>> 16)
    ihdr[2] <- byte (uint32 width >>> 8)
    ihdr[3] <- byte (uint32 width)
    ihdr[4] <- byte (uint32 height >>> 24)
    ihdr[5] <- byte (uint32 height >>> 16)
    ihdr[6] <- byte (uint32 height >>> 8)
    ihdr[7] <- byte (uint32 height)
    ihdr[8] <- 8uy // bit depth
    ihdr[9] <- colorType
    ihdr[10] <- 0uy // compression method (deflate)
    ihdr[11] <- 0uy // filter method (adaptive)
    ihdr[12] <- 0uy // interlace method (none)

    use out = new MemoryStream()
    out.Write(pngSignature, 0, pngSignature.Length)
    writeChunk out chunkIhdr ihdr
    writeChunk out chunkIdat (idatBuf.ToArray())
    writeChunk out chunkIend [||]
    out.ToArray()

/// Encodes an OpenGL bottom-up tightly packed RGB24 buffer into a standard PNG (§5.6).
let encodeRgb24 (width: int) (height: int) (bottomUpRgb: byte[]) : byte[] =
    encode width height 2uy 3 bottomUpRgb

/// Encodes an OpenGL bottom-up tightly packed 8-bit grayscale buffer into a standard PNG (§5.6).
let encodeGray8 (width: int) (height: int) (bottomUpGray: byte[]) : byte[] =
    encode width height 0uy 1 bottomUpGray

// ------------------------------------------------ 5x7 bitmap font & contact sheet

/// 5-bit-wide x 7-row bitmap glyphs (MSB = leftmost pixel of the 5 columns).
let private glyph5x7 (c: char) : byte[] =
    match Char.ToUpperInvariant c with
    | 'A' -> [| 0b01110uy; 0b10001uy; 0b10001uy; 0b11111uy; 0b10001uy; 0b10001uy; 0b10001uy |]
    | 'B' -> [| 0b11110uy; 0b10001uy; 0b10001uy; 0b11110uy; 0b10001uy; 0b10001uy; 0b11110uy |]
    | 'C' -> [| 0b01110uy; 0b10001uy; 0b10000uy; 0b10000uy; 0b10000uy; 0b10001uy; 0b01110uy |]
    | 'D' -> [| 0b11100uy; 0b10010uy; 0b10001uy; 0b10001uy; 0b10001uy; 0b10010uy; 0b11100uy |]
    | 'E' -> [| 0b11111uy; 0b10000uy; 0b10000uy; 0b11110uy; 0b10000uy; 0b10000uy; 0b11111uy |]
    | 'F' -> [| 0b11111uy; 0b10000uy; 0b10000uy; 0b11110uy; 0b10000uy; 0b10000uy; 0b10000uy |]
    | 'G' -> [| 0b01110uy; 0b10001uy; 0b10000uy; 0b10111uy; 0b10001uy; 0b10001uy; 0b01110uy |]
    | 'H' -> [| 0b10001uy; 0b10001uy; 0b10001uy; 0b11111uy; 0b10001uy; 0b10001uy; 0b10001uy |]
    | 'I' -> [| 0b01110uy; 0b00100uy; 0b00100uy; 0b00100uy; 0b00100uy; 0b00100uy; 0b01110uy |]
    | 'J' -> [| 0b00111uy; 0b00010uy; 0b00010uy; 0b00010uy; 0b10010uy; 0b10010uy; 0b01100uy |]
    | 'K' -> [| 0b10001uy; 0b10010uy; 0b10100uy; 0b11000uy; 0b10100uy; 0b10010uy; 0b10001uy |]
    | 'L' -> [| 0b10000uy; 0b10000uy; 0b10000uy; 0b10000uy; 0b10000uy; 0b10000uy; 0b11111uy |]
    | 'M' -> [| 0b10001uy; 0b11011uy; 0b10101uy; 0b10101uy; 0b10001uy; 0b10001uy; 0b10001uy |]
    | 'N' -> [| 0b10001uy; 0b11001uy; 0b10101uy; 0b10011uy; 0b10001uy; 0b10001uy; 0b10001uy |]
    | 'O' -> [| 0b01110uy; 0b10001uy; 0b10001uy; 0b10001uy; 0b10001uy; 0b10001uy; 0b01110uy |]
    | 'P' -> [| 0b11110uy; 0b10001uy; 0b10001uy; 0b11110uy; 0b10000uy; 0b10000uy; 0b10000uy |]
    | 'Q' -> [| 0b01110uy; 0b10001uy; 0b10001uy; 0b10001uy; 0b10101uy; 0b10010uy; 0b01101uy |]
    | 'R' -> [| 0b11110uy; 0b10001uy; 0b10001uy; 0b11110uy; 0b10100uy; 0b10010uy; 0b10001uy |]
    | 'S' -> [| 0b01111uy; 0b10000uy; 0b10000uy; 0b01110uy; 0b00001uy; 0b00001uy; 0b11110uy |]
    | 'T' -> [| 0b11111uy; 0b00100uy; 0b00100uy; 0b00100uy; 0b00100uy; 0b00100uy; 0b00100uy |]
    | 'U' -> [| 0b10001uy; 0b10001uy; 0b10001uy; 0b10001uy; 0b10001uy; 0b10001uy; 0b01110uy |]
    | 'V' -> [| 0b10001uy; 0b10001uy; 0b10001uy; 0b10001uy; 0b10001uy; 0b01010uy; 0b00100uy |]
    | 'W' -> [| 0b10001uy; 0b10001uy; 0b10001uy; 0b10101uy; 0b10101uy; 0b11011uy; 0b10001uy |]
    | 'X' -> [| 0b10001uy; 0b10001uy; 0b01010uy; 0b00100uy; 0b01010uy; 0b10001uy; 0b10001uy |]
    | 'Y' -> [| 0b10001uy; 0b10001uy; 0b01010uy; 0b00100uy; 0b00100uy; 0b00100uy; 0b00100uy |]
    | 'Z' -> [| 0b11111uy; 0b00001uy; 0b00010uy; 0b00100uy; 0b01000uy; 0b10000uy; 0b11111uy |]
    | '0' -> [| 0b01110uy; 0b10001uy; 0b10011uy; 0b10101uy; 0b11001uy; 0b10001uy; 0b01110uy |]
    | '1' -> [| 0b00100uy; 0b01100uy; 0b00100uy; 0b00100uy; 0b00100uy; 0b00100uy; 0b01110uy |]
    | '2' -> [| 0b01110uy; 0b10001uy; 0b00001uy; 0b00110uy; 0b01000uy; 0b10000uy; 0b11111uy |]
    | '3' -> [| 0b11110uy; 0b00001uy; 0b00001uy; 0b01110uy; 0b00001uy; 0b00001uy; 0b11110uy |]
    | '4' -> [| 0b00010uy; 0b00110uy; 0b01010uy; 0b10010uy; 0b11111uy; 0b00010uy; 0b00010uy |]
    | '5' -> [| 0b11111uy; 0b10000uy; 0b11110uy; 0b00001uy; 0b00001uy; 0b10001uy; 0b01110uy |]
    | '6' -> [| 0b01110uy; 0b10000uy; 0b11110uy; 0b10001uy; 0b10001uy; 0b10001uy; 0b01110uy |]
    | '7' -> [| 0b11111uy; 0b00001uy; 0b00010uy; 0b00100uy; 0b01000uy; 0b01000uy; 0b01000uy |]
    | '8' -> [| 0b01110uy; 0b10001uy; 0b10001uy; 0b01110uy; 0b10001uy; 0b10001uy; 0b01110uy |]
    | '9' -> [| 0b01110uy; 0b10001uy; 0b10001uy; 0b01111uy; 0b00001uy; 0b00001uy; 0b01110uy |]
    | '+' -> [| 0b00000uy; 0b00100uy; 0b00100uy; 0b11111uy; 0b00100uy; 0b00100uy; 0b00000uy |]
    | '/' -> [| 0b00001uy; 0b00010uy; 0b00010uy; 0b00100uy; 0b01000uy; 0b01000uy; 0b10000uy |]
    | '-' -> [| 0b00000uy; 0b00000uy; 0b00000uy; 0b11111uy; 0b00000uy; 0b00000uy; 0b00000uy |]
    | _ -> [| 0uy; 0uy; 0uy; 0uy; 0uy; 0uy; 0uy |]

/// Neutral divider crosshair RGB color (#cccccc).
let contactSheetDividerRgb = (0xCCuy, 0xCCuy, 0xCCuy)

/// Corner label text RGB color (#282828).
let contactSheetLabelTextRgb = (0x28uy, 0x28uy, 0x28uy)

let private drawTileLabel
    (sheet: byte[])
    (sheetSize: int)
    (tileSize: int)
    (tileOriginX: int)
    (tileOriginYBottomUp: int)
    (label: string)
    =
    if label.Length > 0 then
        let scale = max 1 (tileSize / 256)
        let margin = 4 * scale
        let padX = 3 * scale
        let padY = 2 * scale
        let textW = (label.Length * 6 - 1) * scale
        let textH = 7 * scale
        let pillX0 = margin
        let pillY0 = margin
        let pillX1 = min tileSize (pillX0 + textW + 2 * padX)
        let pillY1 = min tileSize (pillY0 + textH + 2 * padY)
        let sheetStride = sheetSize * 3

        let inline pixelOffset tx tyTopDown =
            let gx = tileOriginX + tx
            let gyBottomUp = tileOriginYBottomUp + (tileSize - 1 - tyTopDown)
            gyBottomUp * sheetStride + gx * 3

        // Soft light backing pill: 75% white + 25% underlying tile pixel.
        for ty = pillY0 to pillY1 - 1 do
            for tx = pillX0 to pillX1 - 1 do
                let o = pixelOffset tx ty
                sheet[o] <- byte ((int sheet[o] + 3 * 255 + 2) / 4)
                sheet[o + 1] <- byte ((int sheet[o + 1] + 3 * 255 + 2) / 4)
                sheet[o + 2] <- byte ((int sheet[o + 2] + 3 * 255 + 2) / 4)

        let tr, tg, tb = contactSheetLabelTextRgb
        let textX0 = pillX0 + padX
        let textY0 = pillY0 + padY

        for ci = 0 to label.Length - 1 do
            let rows = glyph5x7 label[ci]
            let charX = textX0 + ci * 6 * scale

            for row = 0 to 6 do
                let bits = rows[row]

                for col = 0 to 4 do
                    if (bits &&& (1uy <<< (4 - col))) <> 0uy then
                        for sy = 0 to scale - 1 do
                            let ty = textY0 + row * scale + sy

                            if ty >= 0 && ty < tileSize then
                                for sx = 0 to scale - 1 do
                                    let tx = charX + col * scale + sx

                                    if tx >= 0 && tx < tileSize then
                                        let o = pixelOffset tx ty
                                        sheet[o] <- tr
                                        sheet[o + 1] <- tg
                                        sheet[o + 2] <- tb

/// Composites four square bottom-up RGB24 tiles (`FRONT` TL, `SIDE +90` TR,
/// `3/4 ISO` BL, `TOP` BR) into a `2*tileSize x 2*tileSize` bottom-up RGB24 buffer
/// with corner labels and a 2 px neutral divider crosshair (§5.5).
let composeContactSheet
    (tileSize: int)
    (topLeft: byte[])
    (topRight: byte[])
    (bottomLeft: byte[])
    (bottomRight: byte[])
    : byte[] =
    if tileSize < 64 || tileSize > 4096 then
        invalidArg "tileSize" "contact-sheet tileSize must be in [64, 4096] (sheet size in [128, 8192])"

    let expectedTile = int64 tileSize * int64 tileSize * 3L

    if
        int64 topLeft.Length <> expectedTile
        || int64 topRight.Length <> expectedTile
        || int64 bottomLeft.Length <> expectedTile
        || int64 bottomRight.Length <> expectedTile
    then
        invalidArg "tiles" "each contact-sheet tile must have tileSize * tileSize * 3 bytes"

    let sheetSize = tileSize * 2
    let sheet = Array.zeroCreate<byte> (sheetSize * sheetSize * 3)
    let tileStride = tileSize * 3
    let sheetStride = sheetSize * 3

    let copyTile (src: byte[]) (dstX: int) (dstYBottomUp: int) =
        for y = 0 to tileSize - 1 do
            Buffer.BlockCopy(
                src,
                y * tileStride,
                sheet,
                (dstYBottomUp + y) * sheetStride + dstX * 3,
                tileStride
            )

    copyTile topLeft 0 tileSize
    copyTile topRight tileSize tileSize
    copyTile bottomLeft 0 0
    copyTile bottomRight tileSize 0

    // Corner labels strictly clipped to each tile's bounds.
    drawTileLabel sheet sheetSize tileSize 0 tileSize Camera.frontView.Label
    drawTileLabel sheet sheetSize tileSize tileSize tileSize Camera.sideView.Label
    drawTileLabel sheet sheetSize tileSize 0 0 Camera.isoView.Label
    drawTileLabel sheet sheetSize tileSize tileSize 0 Camera.topView.Label

    // 2 px neutral divider crosshair drawn after labels so quadrant boundaries stay crisp.
    let dr, dg, db = contactSheetDividerRgb

    for y = 0 to sheetSize - 1 do
        let rowBase = y * sheetStride

        for x = tileSize - 1 to tileSize do
            let o = rowBase + x * 3
            sheet[o] <- dr
            sheet[o + 1] <- dg
            sheet[o + 2] <- db

    for y = tileSize - 1 to tileSize do
        let rowBase = y * sheetStride

        for x = 0 to sheetSize - 1 do
            let o = rowBase + x * 3
            sheet[o] <- dr
            sheet[o + 1] <- dg
            sheet[o + 2] <- db

    sheet

/// Composites and PNG-encodes a 2x2 contact sheet from four bottom-up RGB24 tiles (§5.5–§5.6).
let encodeContactSheet
    (tileSize: int)
    (topLeft: byte[])
    (topRight: byte[])
    (bottomLeft: byte[])
    (bottomRight: byte[])
    : byte[] =
    let sheet = composeContactSheet tileSize topLeft topRight bottomLeft bottomRight
    let sheetSize = tileSize * 2
    encodeRgb24 sheetSize sheetSize sheet
