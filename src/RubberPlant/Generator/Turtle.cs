using System;
using System.Collections.Generic;
using System.Numerics;

namespace RubberPlant
{
    public class Turtle
    {
        public IGenerator Generator { get; set; }

        // Angle is stored and consumed internally in radians, but exposed externally as degrees.
        private float m_angle;
        public float Angle
        {
            get { return MathHelpers.RadToDeg(m_angle); }
            set
            {
                m_angle = MathHelpers.DegToRad(value);
                m_turnLeft = Matrix4x4.CreateRotationZ(m_angle);
                m_turnRight = Matrix4x4.CreateRotationZ(-m_angle);
            }
        }

        private float m_stepLength;
        public float StepLength
        {
            get { return m_stepLength; }
            set
            {
                m_stepLength = value;
                m_translate = Matrix4x4.Identity;
                m_translate.Translation = new Vector3(m_stepLength, 0, 0);
            }
        }

        private Matrix4x4 m_currentState;

        private readonly Stack<Matrix4x4> m_states = new Stack<Matrix4x4>();

        // Operation matrices
        private Matrix4x4 m_translate;
        private Matrix4x4 m_turnLeft;
        private Matrix4x4 m_turnRight;

        public Turtle()
        {
            m_currentState = Matrix4x4.CreateRotationZ((float)Math.PI/2);
            StepLength = 1;
        }

        public void Generate(string outputDir, string lsysName, IEnumerable<TurtleCommand> commands)
        {
            if (Generator == null)
            {
                return;
            }

            Generator.StartGenerate(outputDir, lsysName);

            foreach (var command in commands)
            {
                switch (command)
                {
                    case TurtleCommand.Move:
                        m_currentState = m_translate * m_currentState;
                        break;

                    case TurtleCommand.Draw:
                        Generator.DrawSegment(m_currentState, StepLength);
                        m_currentState = m_translate * m_currentState;
                        break;

                    case TurtleCommand.TurnLeft:
                        m_currentState = m_turnLeft * m_currentState;
                        break;

                    case TurtleCommand.TurnRight:
                        m_currentState = m_turnRight * m_currentState;
                        break;

                    case TurtleCommand.StartBranch:
                        m_states.Push(m_currentState);
                        break;

                    case TurtleCommand.EndBranch:
                        if (m_states.Count > 0)
                        {
                            m_currentState = m_states.Pop();
                        }
                        break;

                    case TurtleCommand.Nop:
                        // nothing
                        break;

                    default:
                        throw new ArgumentException(string.Format("Command {0} isn't supported yet.", command));
                }
            }

            Generator.EndGenerate();
        }
    }
}
