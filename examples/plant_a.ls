// From The Algorithmic Beauty of Plants, p. 25

lsystem plant_a
{
    angle = 25.7;
    vocabulary
    {
        F: draw;
    }

    rules
    {
        axiom -> F;
        F -> F[+F]F[-F]F ;
    }
}
