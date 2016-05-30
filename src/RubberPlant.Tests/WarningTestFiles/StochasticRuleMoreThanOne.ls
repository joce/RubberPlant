lsystem StochasticRuleMoreThanOne
{
    angle : 90;

    a: F -> draw;

    axiom : F ; // Start string
    r: F  1 -> FF ,
          1 -> +F+F+ ,
          1 -> -FFF-FF ;
}
