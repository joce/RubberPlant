// From The Algorithmic Beauty of Plants, p. 25

lsystem plant_d
{
    angle : 20;

    a: F -> draw;
    a: X -> nop;

    axiom : X;
    r: X -> F[+X]F[-X]+X ;
    r: F -> FF ;
}
