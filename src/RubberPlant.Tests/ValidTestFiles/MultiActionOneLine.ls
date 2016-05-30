lsystem MultiActionOneLine
{
    angle : -78.432e10;

    a: A, F -> draw;
    a: B, C -> move;

    ax: F-A-B-C ; // Start string
    r: F -> F-F+F++FFFFF-F-F+F ;
    r: C -> ABC;
    r: B -> AAA;
    r: A -> FFFF;
}
