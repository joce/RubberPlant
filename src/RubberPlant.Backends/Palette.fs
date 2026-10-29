/// The color palette shared by every backend (backends.md §4.5): a color
/// index looks the same in SVG, OBJ (MTL) and glTF.
module RubberPlant.Backends.Palette

/// The palette's sRGB colors. Color indices wrap around it (`entry`).
let colors: (byte * byte * byte)[] =
    [| 0x1auy, 0x1auy, 0x1auy // 0: near black
       0x2euy, 0x7duy, 0x32uy // 1: green
       0x6duy, 0x4cuy, 0x41uy // 2: brown
       0xf9uy, 0xa8uy, 0x25uy // 3: yellow
       0xc6uy, 0x28uy, 0x28uy // 4: red
       0x15uy, 0x65uy, 0xc0uy // 5: blue
       0x6auy, 0x1buy, 0x9auy // 6: purple
       0xefuy, 0x6cuy, 0x00uy |] // 7: orange

/// The palette entry (0-7) of a color index. Negative indices wrap too.
let entry (index: int) =
    let n = colors.Length
    ((index % n) + n) % n

/// sRGB color of a color index.
let rgb (index: int) = colors[entry index]

/// CSS color (`#rrggbb`) of a color index.
let hex (index: int) =
    let r, g, b = rgb index
    $"#%02x{r}%02x{g}%02x{b}"
