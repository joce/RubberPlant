lsystem LexerError
{
    angle = -78.432e10;
```  // bad tokens
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
