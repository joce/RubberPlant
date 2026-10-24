/// Character classes from `docs/language.md` §2.4 / Appendix A.1, on Unicode
/// scalar values rather than UTF-16 code units, so supplementary-plane
/// letters (e.g. `𝐀`, U+1D400) count as one letter.
module RubberPlant.Unicode

open System.Text

let isAsciiDigit (c: char) = c >= '0' && c <= '9'

/// Length in UTF-16 units of the scalar starting with `hi` (followed by `lo`),
/// and whether it's a letter. A lone surrogate is a 1-unit non-letter.
let scalarAt (hi: char) (lo: char) : struct (int * bool) =
    if System.Char.IsHighSurrogate hi && System.Char.IsLowSurrogate lo then
        struct (2, Rune.IsLetter(Rune(hi, lo)))
    else
        struct (1, System.Char.IsLetter hi)

/// Length of the name-start character (`Letter | "_"`) at `i`, or 0.
let nameStartLength (s: string) (i: int) =
    if i >= s.Length then 0
    elif s[i] = '_' then 1
    else
        let lo = if i + 1 < s.Length then s[i + 1] else '\000'
        let struct (len, letter) = scalarAt s[i] lo
        if letter then len else 0

/// Length of the name character (`Letter | Digit | "_"`) at `i`, or 0.
let nameCharLength (s: string) (i: int) =
    if i < s.Length && isAsciiDigit s[i] then 1 else nameStartLength s i

/// True if `s` starts with a name-start character.
let startsName (s: string) = nameStartLength s 0 > 0

/// True if `s` ends with a name character.
let endsName (s: string) =
    let n = s.Length

    n > 0
    && (nameCharLength s (n - 1) = 1
        || (n >= 2 && nameStartLength s (n - 2) = 2))
