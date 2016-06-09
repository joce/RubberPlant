using System.Collections.Generic;
using System.Linq;

namespace RubberPlant.Tests
{
    static class Extensions
    {
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
