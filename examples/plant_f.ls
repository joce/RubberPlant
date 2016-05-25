// From The Algorithmic Beauty of Plants, p. 25

lsystem plant_f
{
    angle = 22.5;
    vocabulary
    {
        F: draw;
        X: nop;
    }

    rules
    {
        axiom -> X;
        X -> F-[[X]+X]+F[+FX]-X ;
        F -> FF ;
    }
}
