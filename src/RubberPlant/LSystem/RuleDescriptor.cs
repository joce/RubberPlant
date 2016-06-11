using System.Collections.Generic;
using System.Linq;
using System.Text;

// ReSharper disable NonReadonlyMemberInGetHashCode

namespace RubberPlant
{
    public class RuleDescriptor
    {
        public Atom RuleID { get; set; }

        // PreCondition is expected to be in reverse order,
        // e.g. if you want to match a sting such as
        // ABC D EFG
        // where D is the current atom and ABC the left context, you need to have
        // CBA as precondition. If you're using the parser to create the LSystem
        // (and the descriptors, and the preconditions, etc), the order will be
        // as expected.
        // To be noted, the context is expected to be in reverse order as well.
        public List<Atom> PreCondition { get; set; } = new List<Atom>();
        public List<Atom> PostCondition { get; set; } = new List<Atom>();

        public bool Match(Context context)
        {
            return Match(context, new List<Atom>());
        }

        public bool Match(Context context, IList<Atom> ignores)
        {
            bool left = false;
            bool right = false;
            bool current = (context.Current == RuleID);
            if (current)
            {
                left = MatchLeft(context, ignores);
            }

            if (left)
            {
                right = MatchRight(context, ignores);
            }

            return current && left && right;
        }

        private bool MatchLeft(Context context, IList<Atom> ignores)
        {
            int leftIdx = 0;
            foreach (var pre in PreCondition)
            {
                if (leftIdx >= context.Left.Count)
                {
                    return false;
                }

                if (pre != context.Left[leftIdx])
                {
                    bool skipBranchesAndIgnores;

                    do
                    {
                        // Skip any opening branches
                        while (context.Left[leftIdx] == '[')
                        {
                            leftIdx++;
                            if (leftIdx >= context.Left.Count)
                            {
                                return false;
                            }
                        }

                        // Skip branch in left context.
                        if (context.Left[leftIdx] == ']')
                        {
                            // skip branch.
                            int branchDepth = 0;
                            do
                            {
                                if (context.Left[leftIdx] == ']')
                                {
                                    branchDepth++;
                                }
                                else if (context.Left[leftIdx] == '[')
                                {
                                    branchDepth--;
                                }

                                leftIdx++;
                                if (leftIdx >= context.Left.Count)
                                {
                                    return false;
                                }
                            } while (branchDepth != 0);
                        }

                        // Skip ignores
                        while (ignores.Contains(context.Left[leftIdx]))
                        {
                            leftIdx++;
                            if (leftIdx >= context.Left.Count)
                            {
                                return false;
                            }
                        }

                        skipBranchesAndIgnores = context.Left[leftIdx] == '[' || context.Left[leftIdx] == ']';
                    } while (skipBranchesAndIgnores);
                }

                // Close branch matching
                if (pre != context.Left[leftIdx] && pre == '[')
                {
                    int branchDepth = 0;
                    do
                    {
                        if (context.Left[leftIdx] == ']')
                        {
                            branchDepth++;
                        }
                        else if (context.Left[leftIdx] == '[')
                        {
                            branchDepth--;
                        }
                        leftIdx++;
                        if (leftIdx >= context.Left.Count)
                        {
                            return false;
                        }
                    } while (branchDepth != 0 || context.Left[leftIdx] != '[');
                }

                if (pre != context.Left[leftIdx])
                {
                    return false;
                }

                leftIdx++;
            }

            return true;
        }

        private bool MatchRight(Context context, IList<Atom> ignores)
        {
            int rightIdx = 0;
            foreach (var post in PostCondition)
            {
                if (rightIdx >= context.Right.Count)
                {
                    return false;
                }

                if (post != context.Right[rightIdx])
                {
                    bool skipBranchesAndIgnores;

                    do
                    {
                        // Skip branch in right context.
                        if (context.Right[rightIdx] == '[')
                        {
                            // skip branch.
                            int branchDepth = 0;
                            do
                            {
                                if (context.Right[rightIdx] == '[')
                                {
                                    branchDepth++;
                                }
                                else if (context.Right[rightIdx] == ']')
                                {
                                    branchDepth--;
                                }

                                rightIdx++;
                                if (rightIdx >= context.Right.Count)
                                {
                                    return false;
                                }
                            } while (branchDepth != 0);
                        }

                        // Skip ignores
                        while (ignores.Contains(context.Right[rightIdx]))
                        {
                            rightIdx++;
                            if (rightIdx >= context.Right.Count)
                            {
                                return false;
                            }
                        }

                        skipBranchesAndIgnores = context.Right[rightIdx] == '[';
                    } while (skipBranchesAndIgnores);
                }

                // Close branch matching
                if (post != context.Right[rightIdx] && post == ']')
                {
                    int branchDepth = 0;
                    do
                    {
                        if (context.Right[rightIdx] == '[')
                        {
                            branchDepth++;
                        }
                        else if (context.Right[rightIdx] == ']')
                        {
                            branchDepth--;
                        }
                        rightIdx++;
                        if (rightIdx >= context.Right.Count)
                        {
                            return false;
                        }
                    } while (branchDepth != 0 || context.Right[rightIdx] != ']');
                }

                if (post != context.Right[rightIdx])
                {
                    return false;
                }

                rightIdx++;
            }

            return true;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            if (PreCondition.Any())
            {
                foreach (var atom in PreCondition)
                {
                    sb.Append(atom);
                }
                sb.Append(" < ");
            }
            sb.Append(RuleID);
            if (PostCondition.Any())
            {
                sb.Append(" > ");
                foreach (var atom in PostCondition)
                {
                    sb.Append(atom);
                }
            }
            return sb.ToString();
        }

        public override bool Equals(object obj)
        {
            RuleDescriptor other = obj as RuleDescriptor;
            if (other != null)
            {
                return Equals(other);
            }
            return false;
        }

        protected bool Equals(RuleDescriptor other)
        {
            if (PreCondition.Count != other.PreCondition.Count || PreCondition.Where((t, i) => t != other.PreCondition[i]).Any())
            {
                return false;
            }

            if (PostCondition.Count != other.PostCondition.Count || PostCondition.Where((t, i) => t != other.PostCondition[i]).Any())
            {
                return false;
            }

            return Equals(RuleID, other.RuleID);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (RuleID != null ? RuleID.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ (PreCondition != null ? PreCondition.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ (PostCondition != null ? PostCondition.GetHashCode() : 0);
                return hashCode;
            }
        }

        public static bool operator ==(RuleDescriptor left, RuleDescriptor right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(RuleDescriptor left, RuleDescriptor right)
        {
            return !Equals(left, right);
        }
    }
}
