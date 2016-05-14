lsystem DuplicateAngle
{
    angle = -78.432e10;
    angle = 90;

    vocabulary
    {
        F: draw;
    }

    rules
    {
        axiom -> F-F-F-F ; // Start string
        F -> F-F+F++F;
    }
}
