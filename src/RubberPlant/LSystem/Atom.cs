namespace RubberPlant
{
    public abstract class Atom
    {
        protected bool Equals(Atom other)
        {
            throw new System.NotImplementedException();
        }

        public override bool Equals(object obj)
        {
            throw new System.NotImplementedException();
        }

        public override int GetHashCode()
        {
            throw new System.NotImplementedException();
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
            if ((ruleName >= 'a' && ruleName <= 'z') || (ruleName >= 'A' && ruleName <= 'Z'))
            {
                return new IDAtom(ruleName);
            }

            return new TurtleAtom(ruleName);
        }
    }
}
