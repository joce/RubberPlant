// Adapted from javalin samples (https://github.com/jdf/javalin/tree/master/samples)
// http://local.wasp.uwa.edu.au/~pbourke/fractals/lsys_algae_b/
lsystem algae {

    a: F -> draw;
    a: f -> move ;

    axiom : aF ;
    r: a -> FFFFFy[++++n][----t]fb ;
    r: b -> +FFFFFy[++++n][----t]fc ;
    r: c -> FFFFFy[++++n][----t]fd ;
    r: d -> -FFFFFy[++++n][----t]fe ;
    r: e -> FFFFFy[++++n][----t]fg ;
    r: g -> FFFFFy[+++fa]fh ;
    r: h -> FFFFFy[++++n][----t]fi ;
    r: i -> +FFFFFy[++++n][----t]fj ;
    r: j -> FFFFFy[++++n][----t]fk ;
    r: k -> -FFFFFy[++++n][----t]fl ;
    r: l -> FFFFFy[++++n][----t]fm ;
    r: m -> FFFFFy[---fa]fa ;
    r: n -> ofFFF ;
    r: o -> fFFFp ;
    r: p -> fFFF[-s]q ;
    r: q -> fFFF[-s]r ;
    r: r -> fFFF[-s] ;
    r: s -> fFfF ;
    r: t -> ufFFF ;
    r: u -> fFFFv ;
    r: v -> fFFF[+s]w ;
    r: w -> fFFF[+s]x ;
    r: x -> fFFF[+s] ;
    r: y -> Fy ;

    angle : 12;
}
