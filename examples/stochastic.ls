// Inspired from The Algorithmic Beauty of Plants, pp. 28-29

lsystem stochastic {
    a: F -> draw;
    a: f -> move;
    a: a, b, e -> nop;

    axiom : [F]----e++++[F]----e++++[F]----e++++[F]----e++++[F]----e++++[F];
    r: F  0.33 -> F[+F]F[-F]F ,
          0.33 -> F[+F]F ,
          0.33 -> F[-F]F ;

    r: e -> f;
    r: f -> ff;

    angle : 22.5;
}
