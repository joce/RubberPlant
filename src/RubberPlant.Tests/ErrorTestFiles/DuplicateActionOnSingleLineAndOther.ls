lsystem DuplicateActionOnSingleLineAndOther
{
    angle : -78.432e10;

    a: F, F -> draw;
    a: F -> move;

    axiom : F-F-F-F ; // Start string
    r: F -> F-F+F++F;
}
