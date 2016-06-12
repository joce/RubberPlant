using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RubberPlant
{
    static class AtomExtensions
    {
        public static string ToAtomString(this IEnumerable<Atom> atoms)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var atom in atoms)
            {
                sb.Append(atom.RuleName);
            }

            return sb.ToString();
        }

        public static List<Atom> ToAtoms(this string s)
        {
            return s.Select(c => new Atom(c)).ToList();
        }

        public static List<Atom> ToAtoms(this IEnumerable<char> s)
        {
            return s.Select(c => new Atom(c)).ToList();
        }
    }
}
