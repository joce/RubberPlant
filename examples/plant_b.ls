// From The Algorithmic Beauty of Plants, p. 25

lsystem plant_b
{
    angle : 20;

    a: F -> draw;

    axiom : F;
    r: F -> F[+F]F[-F][F] ;
}
