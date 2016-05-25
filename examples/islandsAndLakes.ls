// From The Algorithmic Beauty of Plants, p. 9

lsystem islandsAndLakes
{
    angle = 90;
    vocabulary
    {
        F: draw;
        f: move;
    }
    rules
    {
        axiom-> F+F+F+F;
        F -> F+f-FF+F+FF+Ff+FF-f+FF-F-FF-Ff-FFF ;
        f -> ffffff ;
    }
}
