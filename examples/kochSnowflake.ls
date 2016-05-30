// From http://mathworld.wolfram.com/KochSnowflake.html

lsystem kochSnowflake
{
    angle : 60;

    a: F -> draw;

    axiom : F--F--F;
    r: F -> F+F--F+F ;
}
