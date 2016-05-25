// From The Algorithmic Beauty of Plants, p. 9

lsystem kochTri
{
    angle = 90;
    vocabulary
    {
        F: draw;
    }
    rules
    {
        axiom-> -F;
        F -> F+F-F-F+F ;
    }
}
