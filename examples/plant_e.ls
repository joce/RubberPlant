// From The Algorithmic Beauty of Plants, p. 25

lsystem plant_e
{
    angle = 25.7;
    vocabulary
    {
        F: draw;
    }
    rules
    {
        axiom -> X ;
        X -> F[+X][-X]FX ;
        F -> FF;
    }
}
