using System.Collections.Generic;
using System.Linq;

namespace RubberPlant
{
    public class LSystem
    {
        public string Name { get; set; }
        public Rule Axiom { get; set; }
        public double Angle { get; set; }
        public List<Rule> Rules { get; set; }
        public Dictionary<IDAtom, TurtleCommand> Vocabulary { get; set; }

        public LSystem()
        {
            Axiom = new Rule();
            Rules = new List<Rule>();
            Vocabulary = new Dictionary<IDAtom, TurtleCommand>();
        }

        public bool HasRule(Atom atom)
        {
            return Rules.Any(r => r.RuleID == atom);
        }

        public Rule GetRule(Atom atom)
        {
            return Rules.First(r => r.RuleID == atom);
        }

        public List<TurtleCommand> Replace(int iterations)
        {
            List<Atom> source = Axiom.Body;
            List<Atom> destination = source;
            for (int i = 0; i < iterations; i++)
            {
                destination = new List<Atom>();
                foreach (var atom in source)
                {
                    Context ctx = new Context() {Current = atom};
                    var rule = Rules.FirstOrDefault(r => r.Match(ctx));
                    if (rule != null)
                    {
                        destination.AddRange(rule.Body);
                    }
                    else
                    {
                        destination.Add(atom);
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
    }
}
