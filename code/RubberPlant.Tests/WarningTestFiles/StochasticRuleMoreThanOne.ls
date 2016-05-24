lsystem StochasticRuleMoreThanOne
{
    angle = 90;

    vocabulary
    {
        F: draw;
    }

    rules
    {
        axiom -> F ; // Start string
        F {
            1 -> FF ;
            1 -> +F+F+ ;
            1 -> -FFF-FF ;
        }
    }
}
