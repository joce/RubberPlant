namespace RubberPlant
{
    public class IDAtom : Atom
    {
        public char RuleName { get; }

        public IDAtom(char ruleName)
        {
            RuleName = ruleName;
        }

        public override bool Equals(object obj)
        {
            return obj.GetType() == typeof(IDAtom) && ((IDAtom)obj).RuleName == RuleName;
        }

        protected bool Equals(IDAtom other)
        {
            return RuleName == other.RuleName;
        }

        public override int GetHashCode()
        {
            return RuleName.GetHashCode();
        }

        public static bool operator ==(IDAtom left, IDAtom right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(IDAtom left, IDAtom right)
        {
            return !Equals(left, right);
        }

        public static implicit operator IDAtom(char ruleName)
        {
            return new IDAtom(ruleName);
        }
    }
}
