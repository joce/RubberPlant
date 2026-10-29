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

/// The palette's colors in linear RGB, for glTF (backends.md §6): the standard
/// sRGB transfer function applied to `colors` / 255, computed once with mpmath and
/// rounded to double. Constants, because Math.Pow can differ in the last bit
/// between platforms (language.md §4.1).
let linear: (float * float * float)[] =
    [| 0.010329823029626936, 0.010329823029626936, 0.010329823029626936
       0.02732089163907489, 0.2050787363903169, 0.03189603307301152
       0.15292615199615014, 0.07227185068231748, 0.05286064702318025
       0.9473065367331996, 0.3915724777497231, 0.01850022012837969
       0.5647115057049289, 0.021219010376003555, 0.021219010376003555
       0.00749903204322617, 0.13013647669036427, 0.527115125705813
       0.14412847085805772, 0.01096009400648824, 0.3231432091129507
       0.863157213454102, 0.14995978981060853, 0.0 |]

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
