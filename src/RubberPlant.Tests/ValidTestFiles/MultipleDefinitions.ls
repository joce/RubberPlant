lsystem Test
{
    angle : -78.432e10;

    a: F -> draw;

    axiom : F-F-F-F ; // Start string
    r: F -> F-F+F++FFFFF-F-F+F ;
}

/* We declare multiple tests in this one file! */
lsystem Test2
{
    angle : 90;

    a: F -> draw;
    a: A -> nop;
    a: B -> move;

    axiom : FFFFFFFF ; // Start string
    r: F -> F+A+F+A+F+A ;
    r: A -> A+F+A +F +B ;

    r: B -> FA /* comment in rule*/ AA ;
}
