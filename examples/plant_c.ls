// From The Algorithmic Beauty of Plants, p. 25

lsystem plant_c
{
    angle = 22.5;
    vocabulary
    {
        F: draw;
    }
    rules
    {
        axiom -> F;
        F -> FF-[-F+F+F]+[+F-F-F] ;
    }
}
