using System;
using System.Collections.Generic;

namespace RubberPlant
{
    public class LSystem
    {
        public string Name { get; set; }
        public List<Atom> Axiom { get; set; }
        public double Angle { get; set; }
        public Dictionary<IDAtom, List<Atom>> Rules { get; set; }
        public Dictionary<IDAtom, List<Tuple<float, List<Atom>>>> StochasticRules { get; set; }
        public Dictionary<IDAtom, TurtleCommand> Vocabulary { get; set; }

        public IRandom Random
        {
            get { return m_random ?? (m_random = new Random()); }
            set { m_random = value;}
        }

        private IRandom m_random;

        public LSystem()
        {
            Axiom = new List<Atom>();
            Rules = new Dictionary<IDAtom, List<Atom>>();
            StochasticRules = new Dictionary<IDAtom, List<Tuple<float, List<Atom>>>>();
            Vocabulary = new Dictionary<IDAtom, TurtleCommand>();
        }

        public bool HasRule(IDAtom atom)
        {
            return Rules.ContainsKey(atom) || StochasticRules.ContainsKey(atom);
        }

        public List<TurtleCommand> Replace(int iterations)
        {
            List<Atom> source = Axiom;
            List<Atom> destination = source;
            for (int i = 0; i < iterations; i++)
            {
                destination = new List<Atom>();
                foreach (var atom in source)
                {
                    if (atom.GetType() == typeof (TurtleAtom))
                    {
                        destination.Add(atom);
                    }
                    else if (atom.GetType() == typeof (IDAtom))
                    {
                        var idAtom = (IDAtom)atom;
                        List<Atom> rule;
                        List<Tuple<float, List<Atom>>> stochasticRule;
                        if (Rules.TryGetValue(idAtom, out rule))
                        {
                            destination.AddRange(rule);
                        }
                        else if (StochasticRules.TryGetValue(idAtom, out stochasticRule))
                        {
                            destination.AddRange(GetRandomRule(stochasticRule));
                        }
                        else
                        {
                            destination.Add(atom);
                        }
                    }
                }
                source = destination;
            }

            return AsTurtleCommands(destination);
        }

        private List<TurtleCommand> AsTurtleCommands(List<Atom> atoms)
        {
            List<TurtleCommand> res = new List<TurtleCommand>();
            foreach (var atom in atoms)
            {
                if (atom.GetType() == typeof (TurtleAtom))
                {
                    var turtleAtom = (TurtleAtom) atom;
                    res.Add(turtleAtom.Command);
                }
                else if (atom.GetType() == typeof (IDAtom))
                {
                    var idAtom = (IDAtom)atom;
                    TurtleCommand command;
                    if (Vocabulary.TryGetValue(idAtom, out command))
                    {
                        res.Add(command);
                    }
                    else
                    {
                        res.Add(TurtleCommand.Nop);
                    }
                }
            }
            return res;
        }

        private List<Atom> GetRandomRule(List<Tuple<float, List<Atom>>> stochasticRule)
        {
            float rnd = (float)Random.NextDouble();
            foreach (var subrule in stochasticRule)
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
