// From The Algorithmic Beauty of Plants, p. 11

lsystem sierpinski
{
    angle : 60;

    a: L, R -> draw;

    axiom : R;
    r: L -> R+L+R ;
    r: R -> L-R-L ;
}
