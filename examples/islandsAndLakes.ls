// From The Algorithmic Beauty of Plants, p. 9

lsystem islandsAndLakes
{
    angle : 90;
    a: F -> draw;
    a: f -> move;

    axiom : F+F+F+F;
    r: F -> F+f-FF+F+FF+Ff+FF-f+FF-F-FF-Ff-FFF ;
    r: f -> ffffff ;
}
