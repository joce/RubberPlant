lsystem DuplicateAction
{
    angle = -78.432e10;

    vocabulary
    {
        F: draw;
        F: move;
    }

    rules
    {
        axiom -> F-F-F-F ; // Start string
        F -> F-F+F++FFFFF-F-F+F ;
    }
}
