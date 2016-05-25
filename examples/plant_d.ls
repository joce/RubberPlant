// From The Algorithmic Beauty of Plants, p. 25

lsystem plant_d
{
    angle = 20;
    vocabulary
    {
        F: draw;
        X: nop;
    }

    rules
    {
        axiom -> X;
        X -> F[+X]F[-X]+X ;
        F -> FF ;
    }
}
