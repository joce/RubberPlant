lsystem MultipleRuleBlocks
{
    angle = -78.432e10;

    vocabulary
    {
        F: draw;
        A: nop;
        B: move;
    }

    rules
    {
        axiom -> F-A-F-A ; // Start string
        F -> F-F+F++FFFFF-F-F+F ;
    }

    // Multiple rule blocks are OK as long as the rules are not deplucated.
    rules
    {
        A -> AFAF--AFAB ;
        B -> BABA ;
    }
}

