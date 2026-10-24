/// A small, immutable, seeded random number generator (SplitMix64).
/// Our own implementation, so results don't depend on the .NET version.
module RubberPlant.Rng

[<Struct>]
type Rng = private { State: uint64 }

let create (seed: int64) = { State = uint64 seed }

/// A uniform float in [0, 1), and the next generator state.
let next (r: Rng) : struct (float * Rng) =
    let s = r.State + 0x9E3779B97F4A7C15UL
    let mutable z = s
    z <- (z ^^^ (z >>> 30)) * 0xBF58476D1CE4E5B9UL
    z <- (z ^^^ (z >>> 27)) * 0x94D049BB133111EBUL
    z <- z ^^^ (z >>> 31)
    struct (float (z >>> 11) * (1.0 / 9007199254740992.0), { State = s })
