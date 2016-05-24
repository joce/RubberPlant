using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace RubberPlant
{
    class Turtle
    {
        public IRenderer Renderer { get; set; }

        private double m_angle;

        // Angle is stored internally in rad, but consumed and exposed externally as degrees.
        public double Angle
        {
            get { return RadToDeg(m_angle); }
            set { m_angle = DegToRad(value); }
        }

        public float StepLength { get; set; }

        class State
        {
            public Vector3 CurrentPos { get; set; }
            public double CurrentAngle { get; set; }

            public State()
            {
                CurrentAngle = Math.PI/2;
            }
        }

        private State m_currentState = new State();

        private Stack<State> m_states = new Stack<State>();

        public Turtle()
        {
            StepLength = 1;
        }

        public void Render(string outputDir, string lsysName, IEnumerable<TurtleCommand> commands)
        {
            if (Renderer == null)
            {
                return;
            }

            Renderer.StartRender(outputDir, lsysName);

            foreach (var command in commands)
            {
                switch (command)
                {
                    case TurtleCommand.Move:
                        {
                            float cos = (float)Math.Cos(m_currentState.CurrentAngle);
                            float sin = (float)Math.Sin(m_currentState.CurrentAngle);
                            m_currentState.CurrentPos = new Vector3(Round(m_currentState.CurrentPos.X + cos*StepLength), Round(m_currentState.CurrentPos.Y + sin*StepLength), 0);
                        }
                        break;

                    case TurtleCommand.Draw:
                        {
                            float cos = (float)Math.Cos(m_currentState.CurrentAngle);
                            float sin = (float)Math.Sin(m_currentState.CurrentAngle);

                            Vector3 nextPos = new Vector3(Round(m_currentState.CurrentPos.X + cos*StepLength), Round(m_currentState.CurrentPos.Y + sin*StepLength), 0);
                            Renderer.DrawSegment(m_currentState.CurrentPos, nextPos);
                            m_currentState.CurrentPos = nextPos;
                        }
                        break;

                    case TurtleCommand.TurnLeft:
                        m_currentState.CurrentAngle += m_angle;
                        if (m_currentState.CurrentAngle > 2*Math.PI)
                        {
                            m_currentState.CurrentAngle -= 2*Math.PI;
                        }
                        break;

                    case TurtleCommand.TurnRight:
                        m_currentState.CurrentAngle -= m_angle;
                        if (m_currentState.CurrentAngle < 0)
                        {
                            m_currentState.CurrentAngle += 2*Math.PI;
                        }
                        break;

                    case TurtleCommand.StartBranch:
                        m_states.Push(m_currentState);
                        var newStsate = new State
                        {
                            CurrentAngle = m_currentState.CurrentAngle,
                            CurrentPos = m_currentState.CurrentPos
                        };
                        m_currentState = newStsate;
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

            Renderer.EndRender();
        }

        private double DegToRad(double deg)
        {
            return deg * Math.PI/180;
        }

        private float RadToDeg(double rad)
        {
            return (float)(rad * 180.0f/Math.PI);
        }

        private float Round(double val)
        {
            return (float) Math.Round(val, 5);
        }
    }
}
