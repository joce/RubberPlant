lsystem problem {
    vocabulary {
        F: draw;
        // L and R are not defined and will be nop, resulting in a whole string
        // of nops and nothing but nops for the turtle to render.
    }
    rules {
        axiom -> L;
        L -> L+R+;
        R -> -L-R;
    }
    angle = 90;
}
