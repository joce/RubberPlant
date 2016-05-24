lsystem MultiActionOneLine
{
    angle = -78.432e10;

    vocabulary
    {
        A, F: draw;
        B, C: move;
    }

    rules
    {
        axiom -> F-A-B-C ; // Start string
        F -> F-F+F++FFFFF-F-F+F ;
        C -> ABC;
        B -> AAA;
        A -> FFFF;
    }
}
