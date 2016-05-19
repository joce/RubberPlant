lsystem StochasticZeroWeight
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
          0.0 -> FF ;
          0.7 -> +F+F+ ;
        }
    }
}
