lsystem Test
{
    angle = -78.432e10;

    vocabulary
    {
        F: draw;
    }

    rules
    {
        axiom -> F-F-F-F ; // Start string
        F -> F-F+F++FFFFF-F-F+F ;
    }
}

/* We declare multiple tests in this one file! */
lsystem Test2
{
    angle = 90;

    vocabulary
    {
        F: draw;
        A: nop;
        B: move;
    }

    rules
    {
        axiom -> FFFFFFFF ; // Start string
        F -> F+A+F+A+F+A ;
        A -> A+F+A +F +B ;

        // Comment in rules block

        B -> FA /* comment in rule*/ AA ;
    }
}
