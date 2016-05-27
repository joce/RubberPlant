using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RubberPlant
{
    public class LSystem
    {
        public string Name { get; set; }
        public Rule Axiom { get; set; }
        public double Angle { get; set; }
        public List<Rule> Rules { get; set; }
        public Dictionary<Atom, TurtleCommand> Vocabulary { get; set; }

        internal static readonly Dictionary<Atom, TurtleCommand> k_implicitTurtleCommands = new Dictionary<Atom, TurtleCommand>();

        static LSystem()
        {
            Type t = typeof(TurtleCommand);
            foreach (TurtleCommand enumVal in Enum.GetValues(typeof(TurtleCommand)))
            {
                FieldInfo field = t.GetField(enumVal.ToString());
                if (field.IsDefined(typeof(CharValueAttribute), false))
                {
                    var attr = (CharValueAttribute)t.GetField(enumVal.ToString()).GetCustomAttributes(typeof(CharValueAttribute), false)[0];
                    k_implicitTurtleCommands[attr.Value] = enumVal;
                }
            }
        }

        public LSystem()
        {
            Axiom = new Rule();
            Rules = new List<Rule>();
            Vocabulary = new Dictionary<Atom, TurtleCommand>(k_implicitTurtleCommands);
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
                TurtleCommand command;
                if (Vocabulary.TryGetValue(atom, out command))
                {
                    res.Add(command);
                }
                else
                {
                    res.Add(TurtleCommand.Nop);
                }
            }
            return res;
        }
    }
}
