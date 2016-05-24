lsystem RuleWithoutAction
{
    angle = -78.432e10;

    vocabulary
    {
        F: draw;
    }

    rules
    {
        axiom -> F-F-F-F ; // Start string
        F -> F-F+F++AFAFAF-F-F+F ;
        A -> FF ;
    }
}
