using System;

[AttributeUsage(AttributeTargets.Field)]
public class CharValueAttribute : Attribute
{
    public char Value { get; }

    public CharValueAttribute(char charValue)
    {
        Value = charValue;
    }
}

namespace RubberPlant
{
    public enum TurtleCommand
    {
        Nop,
        Move,
        Draw,
        [CharValue('%')]
        CutOffBranch,
        [CharValue('^')]
        PitchUp,
        [CharValue('&')]
        PitchDown,
        [CharValue('\\')]
        RollLeft,
        [CharValue('/')]
        RollRight,
        [CharValue('$')]
        RotateToVertical,
        [CharValue('+')]
        TurnLeft,
        [CharValue('-')]
        TurnRight,
        [CharValue('|')]
        TurnAround,
        [CharValue('!')]
        DecrementDiameter,
        [CharValue('[')]
        StartBranch,
        [CharValue(']')]
        EndBranch,
        [CharValue('{')]
        StartPoly,
        [CharValue('}')]
        EndPoly,
        [CharValue('\'')]
        IncrementColorIndex,
        [CharValue('~')]
        PredefinedSurface,
        [CharValue('.')]
        RecordVertex,
    }

    public static class TurtleCommandHelper
    {
        public static TurtleCommand ToCommand(this string str)
        {
            TurtleCommand cmd;
            switch (str)
            {
                case "draw":
                    cmd = TurtleCommand.Draw;
                    break;
                case "move":
                    cmd = TurtleCommand.Move;
                    break;
                case "nop":
                    cmd = TurtleCommand.Nop;
                    break;
                default:
                    throw new ArgumentException(string.Format("Unknown command: {0}", str));
            }

            return cmd;
        }
    }
}
