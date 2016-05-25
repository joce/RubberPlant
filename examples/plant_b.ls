// From The Algorithmic Beauty of Plants, p. 25

lsystem plant_b
{
    angle = 20;
    vocabulary
    {
        F: draw;
    }
    rules
    {
        axiom -> F;
        F -> F[+F]F[-F][F] ;
    }
}
