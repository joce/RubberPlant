// From The Algorithmic Beauty of Plants, p. 25

lsystem plant_c
{
    angle : 22.5;

    a: F -> draw;

    axiom : F;
    r: F -> FF-[-F+F+F]+[+F-F-F] ;
}
