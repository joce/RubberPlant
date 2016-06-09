#if DEBUG_HELPER
using System.Collections.Generic;
using System.Text;

namespace RubberPlant
{
    static class EnumerableExtensions
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
    }
}
#endif
