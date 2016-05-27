namespace RubberPlant
{
    public class Random : IRandom
    {
        private readonly System.Random m_rng;

        public Random(int seed)
        {
            m_rng = new System.Random(seed);
        }

        public Random()
        {
            m_rng = new System.Random();
        }

        public double NextDouble()
        {
            return m_rng.NextDouble();
        }
    }
}
