lsystem CurlyBracesRule
{
    angle = 0.2;

    vocabulary
    {
        F: draw;
    }

    rules
    {
        axiom -> F ; // Start string
        F -> {F-F} ;
    }
}
