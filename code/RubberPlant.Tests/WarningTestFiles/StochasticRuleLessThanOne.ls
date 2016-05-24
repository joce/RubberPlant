lsystem StochasticRuleLessThanOne
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
            0.3 -> FF ;
            0.3 -> +F+F+ ;
        }
    }
}
