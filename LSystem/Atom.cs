namespace RubberPlant
{
    public abstract class Atom
    {
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
