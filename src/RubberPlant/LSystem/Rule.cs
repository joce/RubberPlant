using System;
using System.Collections.Generic;
using System.Linq;

namespace RubberPlant
{
    public class Rule
    {
        private static IRandom s_random;
        public static IRandom Random
        {
            get { return s_random ?? (s_random = new Random()); }
            set { s_random = value; }
        }

        private List<Tuple<float, List<Atom>>> m_bodies = new List<Tuple<float, List<Atom>>>();

        public Atom RuleID { get; set; }
        public List<Atom> Body => m_bodies.Count == 1 ? m_bodies[0].Item2 : GetRandomBody();
        public float TotalWeight => m_bodies.Select(r => r.Item1).Sum();
        public int BodyCount => m_bodies.Count;

        public void AddBody(List<Atom> body)
        {
            AddBody(body, 1);
        }

        public void AddBody(List<Atom> body, float weight)
        {
            m_bodies.Add(new Tuple<float, List<Atom>>(weight, body));
        }

        public void NormalizeWeights()
        {
            if (m_bodies.Count == 0)
            {
                throw new InvalidOperationException("No stochastic rules to normalize on.");
            }

            float totalWeight = m_bodies.Select(r => r.Item1).Sum();
            m_bodies = m_bodies.Select(subrule => new Tuple<float, List<Atom>>(subrule.Item1/totalWeight, subrule.Item2)).ToList();
        }

        public virtual bool Match(Context context)
        {
            return context.Current ==RuleID;
        }

        // For testing purposes
        internal Tuple<float, List<Atom>> this[int i] => m_bodies[i];

        private List<Atom> GetRandomBody()
        {
            var rnd = (float)Random.NextDouble();
            foreach (var subrule in m_bodies)
            {
                if (rnd <= subrule.Item1)
                {
                    return subrule.Item2;
                }
                rnd -= subrule.Item1;
            }
            // TODO create our own exception? Maybe...
            throw new Exception("Impossible state reached.");
        }
    }
}
