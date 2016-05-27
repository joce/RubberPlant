namespace RubberPlant
{
    public class Atom
    {
        public char RuleName { get; }

        public Atom(char ruleName)
        {
            RuleName = ruleName;
        }

        public override bool Equals(object obj)
        {
            return obj.GetType() == typeof(Atom) && ((Atom)obj).RuleName == RuleName;
        }

        protected bool Equals(Atom other)
        {
            return RuleName == other.RuleName;
        }

        public override int GetHashCode()
        {
            return RuleName.GetHashCode();
        }

        public static bool operator ==(Atom left, Atom right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(Atom left, Atom right)
        {
            return !Equals(left, right);
        }

        public static implicit operator Atom(char ruleName)
        {
            return new Atom(ruleName);
        }
    }
}
