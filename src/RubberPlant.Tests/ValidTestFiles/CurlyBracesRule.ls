lsystem CurlyBracesRule
{
    angle : 0.2;

    a: F -> draw;

    axiom : F ; // Start string
    r: F -> {F-F} ;
}
