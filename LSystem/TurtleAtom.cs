using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RubberPlant
{
    public class TurtleAtom : Atom
    {
        public TurtleCommand Command { get; }

        private static readonly Dictionary<char, TurtleCommand> k_Commands = new Dictionary<char, TurtleCommand>();

        static TurtleAtom()
        {
            Type t = typeof(TurtleCommand);
            foreach (TurtleCommand enumVal in Enum.GetValues(typeof(TurtleCommand)))
            {
                FieldInfo field = t.GetField(enumVal.ToString());
                if (field.IsDefined(typeof(CharValueAttribute), false))
                {
                    var attr = (CharValueAttribute)t.GetField(enumVal.ToString()).GetCustomAttributes(typeof(CharValueAttribute), false)[0];
                    k_Commands[attr.Value] = enumVal;
                }
            }
        }

        public TurtleAtom(char commandName)
        {
            if (!k_Commands.Keys.Contains(commandName))
            {
                throw new ArgumentException(string.Format("Unknown turtle command: {0}", commandName));
            }
            Command = k_Commands[commandName];
        }

        public override bool Equals(object obj)
        {
            return obj.GetType() == typeof(TurtleAtom) && ((TurtleAtom)obj).Command == Command;
        }

        protected bool Equals(TurtleAtom other)
        {
            return Command == other.Command;
        }

        public override int GetHashCode()
        {
            return (int)Command;
        }

        public static bool operator ==(TurtleAtom left, TurtleAtom right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(TurtleAtom left, TurtleAtom right)
        {
            return !Equals(left, right);
        }
    }
}
