// From The Algorithmic Beauty of Plants, p. 11

lsystem dragon {
    vocabulary {
        L, R: draw;
    }
    rules {
        axiom -> L;
        L -> L+R+;
        R -> -L-R;
    }
    angle = 90;
}
