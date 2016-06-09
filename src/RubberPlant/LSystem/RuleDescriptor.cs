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
        // where D is the current atom and ABC the predecessor, you need to have
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
            // The following is the same as:
            // return context.Current == RuleID &&
            //        MatchPredecessor(context, ignores) &&
            //        MatchSuccessor(context, ignores);
            //
            // but it's easier to debug.

            bool pred = false;
            bool succ = false;
            bool current = (context.Current == RuleID);
            if (current)
                pred = MatchPredecessor(context, ignores);
            if (pred)
                succ = MatchSuccessor(context, ignores);

            return current && pred && succ;
        }

        private bool MatchPredecessor(Context context, IList<Atom> ignores)
        {
            int predIdx = 0;
            foreach (var pre in PreCondition)
            {
                if (predIdx >= context.Predecessors.Count)
                {
                    return false;
                }

                if (pre != context.Predecessors[predIdx])
                {
                    bool skipBranchesAndIgnores;

                    do
                    {
                        // Skip any opening branches
                        while (context.Predecessors[predIdx] == '[')
                        {
                            predIdx++;
                            if (predIdx >= context.Predecessors.Count)
                            {
                                return false;
                            }
                        }

                        // Skip branch in predecessor.
                        if (context.Predecessors[predIdx] == ']')
                        {
                            // skip branch.
                            int branchDepth = 0;
                            do
                            {
                                if (context.Predecessors[predIdx] == ']')
                                {
                                    branchDepth++;
                                }
                                else if (context.Predecessors[predIdx] == '[')
                                {
                                    branchDepth--;
                                }

                                predIdx++;
                                if (predIdx >= context.Predecessors.Count)
                                {
                                    return false;
                                }
                            } while (branchDepth != 0);
                        }

                        // Skip ignores
                        while (ignores.Contains(context.Predecessors[predIdx]))
                        {
                            predIdx++;
                            if (predIdx >= context.Predecessors.Count)
                            {
                                return false;
                            }
                        }

                        skipBranchesAndIgnores = context.Predecessors[predIdx] == '[' || context.Predecessors[predIdx] == ']';
                    } while (skipBranchesAndIgnores);
                }

                // Close branch matching
                if (pre != context.Predecessors[predIdx] && pre == '[')
                {
                    int branchDepth = 0;
                    do
                    {
                        if (context.Predecessors[predIdx] == ']')
                        {
                            branchDepth++;
                        }
                        else if (context.Predecessors[predIdx] == '[')
                        {
                            branchDepth--;
                        }
                        predIdx++;
                        if (predIdx >= context.Predecessors.Count)
                        {
                            return false;
                        }
                    } while (branchDepth != 0 || context.Predecessors[predIdx] != '[');
                }

                if (pre != context.Predecessors[predIdx])
                {
                    return false;
                }

                predIdx++;
            }

            return true;
        }

        private bool MatchSuccessor(Context context, IList<Atom> ignores)
        {
            int succIdx = 0;
            foreach (var post in PostCondition)
            {
                if (succIdx >= context.Successors.Count)
                {
                    return false;
                }

                if (post != context.Successors[succIdx])
                {
                    bool skipBranchesAndIgnores;

                    do
                    {
                        // Skip branch in successor.
                        if (context.Successors[succIdx] == '[')
                        {
                            // skip branch.
                            int branchDepth = 0;
                            do
                            {
                                if (context.Successors[succIdx] == '[')
                                {
                                    branchDepth++;
                                }
                                else if (context.Successors[succIdx] == ']')
                                {
                                    branchDepth--;
                                }

                                succIdx++;
                                if (succIdx >= context.Successors.Count)
                                {
                                    return false;
                                }
                            } while (branchDepth != 0);
                        }

                        // Skip ignores
                        while (ignores.Contains(context.Successors[succIdx]))
                        {
                            succIdx++;
                            if (succIdx >= context.Successors.Count)
                            {
                                return false;
                            }
                        }

                        skipBranchesAndIgnores = context.Successors[succIdx] == '[';
                    } while (skipBranchesAndIgnores);
                }

                // Close branch matching
                if (post != context.Successors[succIdx] && post == ']')
                {
                    int branchDepth = 0;
                    do
                    {
                        if (context.Successors[succIdx] == '[')
                        {
                            branchDepth++;
                        }
                        else if (context.Successors[succIdx] == ']')
                        {
                            branchDepth--;
                        }
                        succIdx++;
                        if (succIdx >= context.Successors.Count)
                        {
                            return false;
                        }
                    } while (branchDepth != 0 || context.Successors[succIdx] != ']');
                }

                if (post != context.Successors[succIdx])
                {
                    return false;
                }

                succIdx++;
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
