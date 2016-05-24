lsystem MultipleActionBlocks
{
    angle = -78.432e10;

    vocabulary
    {
        F: draw;
    }

    rules
    {
        axiom -> F-A-F-A ; // Start string
        F -> F-F+F++FFFFF-F-F+F ;
    }

    vocabulary
    {
        A: move;
        B: nop;
    }

    // Multiple rule blocks are OK as long as the rules are not deplucated.
    rules
    {
        A -> AFAF--AFAB ;
        B -> BABA ;
    }
}

