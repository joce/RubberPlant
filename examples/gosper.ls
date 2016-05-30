// From The Algorithmic Beauty of Plants, p. 12

lsystem gosper
{
    angle : 60;

    a: L -> draw;
    a: R -> draw;

    axiom : L;
    r: L -> L+R++R-L--LL-R+ ;
    r: R -> -L+RR++R+L--L-R ;

}
