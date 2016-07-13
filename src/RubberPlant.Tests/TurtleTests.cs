using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Moq;
using NUnit.Framework;

namespace RubberPlant.Tests
{
    [TestFixture]
    class TurtleTests
    {
        private Matrix4x4 m_heading;
        private Matrix4x4 m_step;

        private const float k_tolerance = 0.000000001f;

        [SetUp]
        public void SetUp()
        {
            m_heading = Matrix4x4.CreateRotationZ((float)Math.PI/2);
            m_step = Matrix4x4.Identity;
            m_step.Translation = Vector3.UnitX;
        }

        [Test]
        public void TurtleDoesNotCrashWhenGeneratorIsNotSet()
        {
            var turtle = new Turtle();
            turtle.Generate("", "", new List<TurtleCommand> {TurtleCommand.Draw});
        }

        [Test]
        public void RenererCallsAreMadeAsAexpected()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()));

            var turtle = new Turtle {Generator = generator.Object};

            string outputDir = @"C:\Some\Path\To\Put\Generate\Result";
            string lSystemName = "SomeName";

            turtle.Generate(outputDir, lSystemName, new List<TurtleCommand> {TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw});

            generator.Verify(r => r.StartGenerate(It.Is<string>(v => v == outputDir), It.Is<string>(v => v == lSystemName)), Times.Once());
            generator.Verify(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()), Times.Exactly(3));
            generator.Verify(r => r.EndGenerate(), Times.Once());
        }
        [Test]
        public void DrawSegment()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()));

            var turtle = new Turtle {Generator = generator.Object};

            turtle.Generate("", "", new List<TurtleCommand> {TurtleCommand.Draw});

            generator.Verify(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Matrix4x4>(v => v == m_heading), It.Is<float>(v => Math.Abs(v - 1) < k_tolerance)), Times.Once);
        }

        [Test]
        public void MoveWithoutDrawing()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()));

            var turtle = new Turtle {Generator = generator.Object};

            // We need to move then draw to actually get something from the generator.
            turtle.Generate("", "", new List<TurtleCommand> {TurtleCommand.Move, TurtleCommand.Draw});

            // We've moved forward before the draw and this needs to be represented in the heading.
            m_heading = m_step * m_heading;

            generator.Verify(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Matrix4x4>(v => v == m_heading), It.Is<float>(v => Math.Abs(v - 1) < k_tolerance)), Times.Once);
        }

        [Test]
        public void Turn90DegreesLeft()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()));

            var turtle = new Turtle {Generator = generator.Object, Angle = 90};

            // We need to turn then draw to actually get something from the generator.
            turtle.Generate("", "", new List<TurtleCommand> {TurtleCommand.TurnLeft, TurtleCommand.Draw});

            float angle = MathHelpers.DegToRad(90);
            m_heading = Matrix4x4.CreateRotationZ(angle) * m_heading;

            generator.Verify(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Matrix4x4>(v => v == m_heading), It.Is<float>(v => Math.Abs(v - 1) < k_tolerance)), Times.Once);
        }

        [Test]
        public void Turn90DegreesRight()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()));

            var turtle = new Turtle {Generator = generator.Object, Angle = 90};

            // We need to turn then draw to actually get something from the generator.
            turtle.Generate("", "", new List<TurtleCommand> {TurtleCommand.TurnRight, TurtleCommand.Draw});

            float angle = MathHelpers.DegToRad(90);
            m_heading = Matrix4x4.CreateRotationZ(-angle) * m_heading;

            generator.Verify(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Matrix4x4>(v => v == m_heading), It.Is<float>(v => Math.Abs(v - 1) < k_tolerance)), Times.Once);
        }

        [Test]
        public void Turn30DegreesLeft()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()));

            var turtle = new Turtle {Generator = generator.Object, Angle = 30};

            // We need to turn then draw to actually get something from the generator.
            turtle.Generate("", "", new List<TurtleCommand> {TurtleCommand.TurnLeft, TurtleCommand.Draw});

            float angle = MathHelpers.DegToRad(30);
            m_heading = Matrix4x4.CreateRotationZ(angle) * m_heading;

            generator.Verify(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Matrix4x4>(v => v == m_heading), It.Is<float>(v => Math.Abs(v - 1) < k_tolerance)), Times.Once);
        }

        [Test]
        public void Turn30DegreesRight()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()));

            var turtle = new Turtle {Generator = generator.Object, Angle = 30};

            // We need to turn then draw to actually get something from the generator.
            turtle.Generate("", "", new List<TurtleCommand> {TurtleCommand.TurnRight, TurtleCommand.Draw});

            float angle = MathHelpers.DegToRad(30);
            m_heading = Matrix4x4.CreateRotationZ(-angle) * m_heading;

            generator.Verify(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Matrix4x4>(v => v == m_heading), It.Is<float>(v => Math.Abs(v - 1) < k_tolerance)), Times.Once);
        }

        [Test]
        public void TurnMoreThan360DegreesLeft()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()));

            var turtle = new Turtle {Generator = generator.Object, Angle = 75};

            // We need to turn then draw to actually get something from the generator.
            turtle.Generate("", "", new List<TurtleCommand> {TurtleCommand.TurnLeft, TurtleCommand.TurnLeft,
                                                             TurtleCommand.TurnLeft, TurtleCommand.TurnLeft,
                                                             TurtleCommand.TurnLeft, TurtleCommand.Draw});

            float angle = MathHelpers.DegToRad(5 * 75);
            m_heading = Matrix4x4.CreateRotationZ(angle) * m_heading;

            generator.Verify(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Matrix4x4>(v => v.Round(5) == m_heading.Round(5)), It.Is<float>(v => Math.Abs(v - 1) < k_tolerance)), Times.Once);
        }

        [Test]
        public void TurnMoreThan360DegreesRight()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()));

            var turtle = new Turtle { Generator = generator.Object, Angle = 75 };

            // We need to turn then draw to actually get something from the generator.
            turtle.Generate("", "", new List<TurtleCommand> {TurtleCommand.TurnRight, TurtleCommand.TurnRight,
                                                             TurtleCommand.TurnRight, TurtleCommand.TurnRight,
                                                             TurtleCommand.TurnRight, TurtleCommand.Draw});

            float angle = MathHelpers.DegToRad(5 * 75);
            m_heading = Matrix4x4.CreateRotationZ(-angle) * m_heading;

            generator.Verify(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Matrix4x4>(v => v.Round(5) == m_heading.Round(5)), It.Is<float>(v => Math.Abs(v - 1) < k_tolerance)), Times.Once);
        }

        [Test]
        [SuppressMessage("ReSharper", "InconsistentNaming")] // For 1st, 2nd, 3rd, etc.
        public void ActOnSimpleCommandString()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()));

            var turtle = new Turtle {Generator = generator.Object, Angle = 90};

            // We need to turn then draw to actually get something from the generator.
            turtle.Generate("", "", new List<TurtleCommand>
            {
                TurtleCommand.TurnRight, TurtleCommand.Draw, // (1, 0, 0)
                TurtleCommand.TurnRight, TurtleCommand.Draw, // (1, -1, 0)
                TurtleCommand.TurnRight, TurtleCommand.Draw, // (0, -1, 0)
                TurtleCommand.TurnRight, TurtleCommand.Move, // (0, 0, 0) // no draw
                TurtleCommand.TurnLeft,  TurtleCommand.Draw, // (-1, 0, 0)
                TurtleCommand.TurnLeft,  TurtleCommand.Draw, // (-1, -1, 0)
                TurtleCommand.TurnLeft,  TurtleCommand.Draw, // (0, -1, 0)
            });

            generator.Verify(r => r.DrawSegment(It.IsAny<Matrix4x4>(), It.IsAny<float>()), Times.Exactly(6));

            float angle = MathHelpers.DegToRad(90);
            Matrix4x4 translataion = Matrix4x4.Identity;
            translataion.Translation = Vector3.UnitX;
            Matrix4x4 at1stDraw = Matrix4x4.CreateRotationZ(-angle) * m_heading;
            Matrix4x4 at2ndDraw = Matrix4x4.CreateRotationZ(-angle) * translataion * at1stDraw;
            Matrix4x4 at3rdDraw = Matrix4x4.CreateRotationZ(-angle) * translataion * at2ndDraw;
            Matrix4x4 atMove    = Matrix4x4.CreateRotationZ(-angle) * translataion * at3rdDraw;
            Matrix4x4 at4thDraw = Matrix4x4.CreateRotationZ(angle)  * translataion * atMove;
            Matrix4x4 at5thDraw = Matrix4x4.CreateRotationZ(angle)  * translataion * at4thDraw;
            Matrix4x4 at6thDraw = Matrix4x4.CreateRotationZ(angle)  * translataion * at5thDraw;

            generator.Verify(r => r.DrawSegment(It.Is<Matrix4x4>(v => v.Round(5) == at1stDraw.Round(5)), It.Is<float>(v => Math.Abs(v - 1) < k_tolerance)), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Matrix4x4>(v => v.Round(5) == at2ndDraw.Round(5)), It.Is<float>(v => Math.Abs(v - 1) < k_tolerance)), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Matrix4x4>(v => v.Round(5) == at3rdDraw.Round(5)), It.Is<float>(v => Math.Abs(v - 1) < k_tolerance)), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Matrix4x4>(v => v.Round(5) == at4thDraw.Round(5)), It.Is<float>(v => Math.Abs(v - 1) < k_tolerance)), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Matrix4x4>(v => v.Round(5) == at5thDraw.Round(5)), It.Is<float>(v => Math.Abs(v - 1) < k_tolerance)), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Matrix4x4>(v => v.Round(5) == at6thDraw.Round(5)), It.Is<float>(v => Math.Abs(v - 1) < k_tolerance)), Times.Once);
        }
    }
}
