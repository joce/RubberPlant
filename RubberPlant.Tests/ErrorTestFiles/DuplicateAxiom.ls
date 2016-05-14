lsystem DuplicateAxiom
{
    angle = -78.432e10;

    vocabulary
    {
        F: draw;
    }

    rules
    {
        axiom -> F-F-F-F ; // Start string
        axiom -> FFFFF ; // Nope!
        F -> F-F+F++F;
    }
}
