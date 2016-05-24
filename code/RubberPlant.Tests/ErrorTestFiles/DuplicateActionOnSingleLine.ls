lsystem DuplicateActionOnSingleLineAndOther
{
    angle = -78.432e10;

    vocabulary
    {
        F, F : draw;
    }

    rules
    {
        axiom -> F-F-F-F ; // Start string
        F -> F-F+F++F;
    }
}