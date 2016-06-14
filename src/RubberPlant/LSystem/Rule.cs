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

        private List<Tuple<float, List<Atom>>> m_successors = new List<Tuple<float, List<Atom>>>();

        public RulePredecessor Predecessor { get; } = new RulePredecessor();
        public List<Atom> Successor => m_successors.Count == 1 ? m_successors[0].Item2 : GetRandomSuccessor();
        public float TotalWeight => m_successors.Select(r => r.Item1).Sum();
        public int SuccessorCount => m_successors.Count;

        public void AddSuccessor(List<Atom> body)
        {
            AddSuccessor(body, 1);
        }

        public void AddSuccessor(List<Atom> body, float weight)
        {
            m_successors.Add(new Tuple<float, List<Atom>>(weight, body));
        }

        public void NormalizeWeights()
        {
            if (m_successors.Count == 0)
            {
                throw new InvalidOperationException("No stochastic rules to normalize on.");
            }

            float totalWeight = m_successors.Select(r => r.Item1).Sum();
            m_successors = m_successors.Select(subrule => new Tuple<float, List<Atom>>(subrule.Item1/totalWeight, subrule.Item2)).ToList();
        }

        public bool Match(EvalContext evalContext, IList<Atom> ignores)
        {
            return Predecessor.Match(evalContext, ignores);
        }

        public override string ToString()
        {
            return Predecessor.ToString();
        }

        // For testing purposes
        internal Tuple<float, List<Atom>> this[int i] => m_successors[i];

        private List<Atom> GetRandomSuccessor()
        {
            var rnd = (float)Random.NextDouble();
            foreach (var subrule in m_successors)
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
