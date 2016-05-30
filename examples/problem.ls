lsystem problem {


    a: F -> draw;
        // L and R are not defined and will be nop, resulting in a whole string
        // of nops and nothing but nops for the turtle to render.

    axiom : L;
    r: L -> L+R+;
    r: R -> -L-R;

    angle : 90;
}
