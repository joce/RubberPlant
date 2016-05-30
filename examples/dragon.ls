// From The Algorithmic Beauty of Plants, p. 11

lsystem dragon {

    a: L, R -> draw;

    axiom : L;
    r: L -> L+R+;
    r: R -> -L-R;

    angle : 90;
}
