// From The Algorithmic Beauty of Plants, p. 12

lsystem gosper
{
    angle = 60;
    vocabulary
    {
        L: draw;
        R: draw;
    }

    rules
    {
        axiom -> L;
        L -> L+R++R-L--LL-R+ ;
        R -> -L+RR++R+L--L-R ;
    }
}
