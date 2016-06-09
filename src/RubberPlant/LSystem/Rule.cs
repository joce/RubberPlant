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

        private List<Tuple<float, List<Atom>>> m_replacements = new List<Tuple<float, List<Atom>>>();

        public RuleDescriptor Descriptor { get; } = new RuleDescriptor();
        public List<Atom> Replacement => m_replacements.Count == 1 ? m_replacements[0].Item2 : GetRandomReplacement();
        public float TotalWeight => m_replacements.Select(r => r.Item1).Sum();
        public int ReplacementCount => m_replacements.Count;

        public void AddReplacement(List<Atom> body)
        {
            AddReplacement(body, 1);
        }

        public void AddReplacement(List<Atom> body, float weight)
        {
            m_replacements.Add(new Tuple<float, List<Atom>>(weight, body));
        }

        public void NormalizeWeights()
        {
            if (m_replacements.Count == 0)
            {
                throw new InvalidOperationException("No stochastic rules to normalize on.");
            }

            float totalWeight = m_replacements.Select(r => r.Item1).Sum();
            m_replacements = m_replacements.Select(subrule => new Tuple<float, List<Atom>>(subrule.Item1/totalWeight, subrule.Item2)).ToList();
        }

        public bool Match(Context context, IList<Atom> ignores)
        {
            return Descriptor.Match(context, ignores);
        }

        public override string ToString()
        {
            return Descriptor.ToString();
        }

        // For testing purposes
        internal Tuple<float, List<Atom>> this[int i] => m_replacements[i];

        private List<Atom> GetRandomReplacement()
        {
            var rnd = (float)Random.NextDouble();
            foreach (var subrule in m_replacements)
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
