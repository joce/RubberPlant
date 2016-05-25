// From The Algorithmic Beauty of Plants, p. 11

lsystem sierpinski
{
    angle = 60;
    vocabulary
    {
        L, R: draw;
    }
    rules
    {
        axiom -> R;
        L -> R+L+R ;
        R -> L-R-L ;
    }
}
