// From http://mathworld.wolfram.com/KochSnowflake.html

lsystem kochSnowflake
{
    angle = 60;
    vocabulary
    {
        F: draw;
    }
    rules
    {
        axiom-> F--F--F;
        F -> F+F--F+F ;
    }
}
