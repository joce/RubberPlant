// From The Algorithmic Beauty of Plants, p. 25

lsystem plant_f
{
    angle : 22.5;

    a: F -> draw;
    a: X -> nop;

    axiom : X;
    r: X -> F-[[X]+X]+F[+FX]-X ;
    r: F -> FF ;
}
