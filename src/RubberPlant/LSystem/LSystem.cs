using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RubberPlant
{
    public class LSystem
    {
        public string Name { get; set; }
        public double Angle { get; set; }
        public Rule Axiom { get; set; } = new Rule();
        public List<Rule> Rules { get; set; } = new List<Rule>();
        public Dictionary<Atom, TurtleCommand> Vocabulary { get; set; }

        public List<Atom> MatchIgnores { get; set; } = new List<Atom>();

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
            Vocabulary = new Dictionary<Atom, TurtleCommand>(k_implicitTurtleCommands);
        }

        public bool HasRule(Atom atom)
        {
            return Rules.Any(r => r.Descriptor.RuleID == atom);
        }

        public bool HasRule(RuleDescriptor desc)
        {
            return Rules.Any(r => r.Descriptor == desc);
        }

        public IEnumerable<Rule> GetRules(Atom atom)
        {
            return Rules.Where(r => r.Descriptor.RuleID == atom);
        }

        public Rule GetRule(RuleDescriptor desc)
        {
            return Rules.First(r => r.Descriptor == desc);
        }

        public List<Atom> Replace(int iterations)
        {
            List<Atom> source = Axiom.Replacement;
            List<Atom> destination = source;
            for (int i = 0; i < iterations; i++)
            {
#if DEBUG_HELPER
                string debugString = source.ToAtomString();
#endif
                destination = new List<Atom>();
                EvalContext ctx = new EvalContext()
                {
                    Left = new List<Atom>(),
                    Current = null,
                    Right = source
                };

                while (source.Any())
                {
                    if (ctx.Current != null)
                    {
                        ctx.Left.Insert(0, ctx.Current);
                    }
                    ctx.Current = source[0];
                    source.RemoveAt(0);
                    var rule = Rules.FirstOrDefault(r => r.Match(ctx, MatchIgnores));
                    if (rule != null)
                    {
                        destination.AddRange(rule.Replacement);
                    }
                    else
                    {
                        destination.Add(ctx.Current);
                    }
                }
                source = destination;
            }

            return destination;
        }

        public List<TurtleCommand> ReplaceAndTranslate(int iterations)
        {
            return TranslateToTurtleCommands(Replace(iterations));
        }

        internal List<TurtleCommand> TranslateToTurtleCommands(List<Atom> atoms)
        {
            List<TurtleCommand> res = new List<TurtleCommand>();
            foreach (var atom in atoms)
            {
                TurtleCommand command;
                if (Vocabulary.TryGetValue(atom, out command))
                {
                    res.Add(command);
                }
            }
            return res;
        }
    }
}
