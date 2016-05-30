// From The Algorithmic Beauty of Plants, p. 25

lsystem plant_e
{
    angle : 25.7;

    a: F -> draw;

    axiom : X ;
    r: X -> F[+X][-X]FX ;
    r: F -> FF;
}
