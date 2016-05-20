lsystem MissingAngle
{
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
