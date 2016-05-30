lsystem StochasticRuleLessThanOne
{
    angle : 90;

    a: F -> draw;

    axiom : F ; // Start string
    r: F 0.3 -> FF ,
         0.3 -> +F+F+ ;
}
