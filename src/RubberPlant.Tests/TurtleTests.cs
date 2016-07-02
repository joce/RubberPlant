using System;
using System.Collections.Generic;
using System.Numerics;
using Moq;
using NUnit.Framework;

namespace RubberPlant.Tests
{
    [TestFixture]
    class TurtleTests
    {
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
            generator.Setup(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()));

            var turtle = new Turtle {Generator = generator.Object};

            string outputDir = @"C:\Some\Path\To\Put\Generate\Result";
            string lSystemName = "SomeName";

            turtle.Generate(outputDir, lSystemName, new List<TurtleCommand> {TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw});

            generator.Verify(r => r.StartGenerate(It.Is<string>(v => v == outputDir), It.Is<string>(v => v == lSystemName)), Times.Once());
            generator.Verify(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()), Times.Exactly(3));
            generator.Verify(r => r.EndGenerate(), Times.Once());
        }

        [Test]
        public void DrawSegment()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()));

            var turtle = new Turtle {Generator = generator.Object};

            turtle.Generate("", "", new List<TurtleCommand> {TurtleCommand.Draw});

            generator.Verify(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Vector3>(v => v == new Vector3(0, 0, 0)), It.Is<Vector3>(v => v == new Vector3(0, 1, 0))), Times.Once);
        }

        [Test]
        public void MoveWithoutDrawing()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()));

            var turtle = new Turtle {Generator = generator.Object};

            // We need to move then draw to actually get something from the generator.
            turtle.Generate("", "", new List<TurtleCommand> {TurtleCommand.Move, TurtleCommand.Draw});

            generator.Verify(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Vector3>(v => v == new Vector3(0, 1, 0)), It.Is<Vector3>(v => v == new Vector3(0, 2, 0))), Times.Once);
        }

        [Test]
        public void Turn90DegreesLeft()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()));

            var turtle = new Turtle {Generator = generator.Object, Angle = 90};

            // We need to turn then draw to actually get something from the generator.
            turtle.Generate("", "", new List<TurtleCommand> {TurtleCommand.TurnLeft, TurtleCommand.Draw});

            generator.Verify(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Vector3>(v => v == new Vector3(0, 0, 0)), It.Is<Vector3>(v => v == new Vector3(-1, 0, 0))), Times.Once);
        }

        [Test]
        public void Turn90DegreesRight()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()));

            var turtle = new Turtle {Generator = generator.Object, Angle = 90};

            // We need to turn then draw to actually get something from the generator.
            turtle.Generate("", "", new List<TurtleCommand> {TurtleCommand.TurnRight, TurtleCommand.Draw});

            generator.Verify(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Vector3>(v => v == new Vector3(0, 0, 0)), It.Is<Vector3>(v => v == new Vector3(1, 0, 0))), Times.Once);
        }

        [Test]
        public void Turn30DegreesLeft()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()));

            var turtle = new Turtle {Generator = generator.Object, Angle = 30};

            // We need to turn then draw to actually get something from the generator.
            turtle.Generate("", "", new List<TurtleCommand> {TurtleCommand.TurnLeft, TurtleCommand.Draw});

            // Start angle is 90 degrees. Add 30 (turn left) == 120 degrees == 2pi/3
            double angle = 2*Math.PI/3;

            // The turtle rounds to the 5 decimal.
            float xRes = (float) Math.Round(Math.Cos(angle), 5);
            float yRes = (float) Math.Round(Math.Sin(angle), 5);

            generator.Verify(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Vector3>(v => v == new Vector3(0, 0, 0)), It.Is<Vector3>(v => v == new Vector3(xRes, yRes, 0))), Times.Once);
        }

        [Test]
        public void Turn30DegreesRight()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()));

            var turtle = new Turtle {Generator = generator.Object, Angle = 30};

            // We need to turn then draw to actually get something from the generator.
            turtle.Generate("", "", new List<TurtleCommand> {TurtleCommand.TurnRight, TurtleCommand.Draw});

            // Start angle is 90 degrees. Sub 30 (turn right) == 60 degrees == pi/3
            double angle = Math.PI/3;

            // The turtle rounds to the 5 decimal.
            float xRes = (float)Math.Round(Math.Cos(angle), 5);
            float yRes = (float)Math.Round(Math.Sin(angle), 5);

            generator.Verify(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Vector3>(v => v == new Vector3(0, 0, 0)), It.Is<Vector3>(v => v == new Vector3(xRes, yRes, 0))), Times.Once);
        }

        [Test]
        public void TurnMoreThan360DegreesRight()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()));

            var turtle = new Turtle {Generator = generator.Object, Angle = 75};

            // We need to turn then draw to actually get something from the generator.
            turtle.Generate("", "", new List<TurtleCommand> {TurtleCommand.TurnRight, TurtleCommand.TurnRight,
                                                           TurtleCommand.TurnRight, TurtleCommand.TurnRight,
                                                           TurtleCommand.TurnRight, TurtleCommand.Draw});

            // Start angle is 90 degrees. Sub (turn right) 5 * 75 => 375 => 15. 90 - 15 = 75
            double angle = 75.0 * Math.PI / 180.0;

            // The turtle rounds to the 5 decimal.
            float xRes = (float)Math.Round(Math.Cos(angle), 5);
            float yRes = (float)Math.Round(Math.Sin(angle), 5);

            generator.Verify(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Vector3>(v => v == new Vector3(0, 0, 0)), It.Is<Vector3>(v => v == new Vector3(xRes, yRes, 0))), Times.Once);
        }

        [Test]
        public void TurnMoreThan360DegreesLeft()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()));

            var turtle = new Turtle {Generator = generator.Object, Angle = 75};

            // We need to turn then draw to actually get something from the generator.
            turtle.Generate("", "", new List<TurtleCommand> {TurtleCommand.TurnLeft, TurtleCommand.TurnLeft,
                                                           TurtleCommand.TurnLeft, TurtleCommand.TurnLeft,
                                                           TurtleCommand.TurnLeft, TurtleCommand.Draw});

            // Start angle is 90 degrees. Add (turn left) 5 * 75 => 375 => 15. 90 + 15 = 105
            double angle = 105.0 * Math.PI / 180.0;

            // The turtle rounds to the 5 decimal.
            float xRes = (float)Math.Round(Math.Cos(angle), 5);
            float yRes = (float)Math.Round(Math.Sin(angle), 5);

            generator.Verify(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Vector3>(v => v == new Vector3(0, 0, 0)), It.Is<Vector3>(v => v == new Vector3(xRes, yRes, 0))), Times.Once);
        }

        [Test]
        public void ActOnSimpleCommandString()
        {
            Mock<IGenerator> generator = new Mock<IGenerator>();
            generator.Setup(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()));

            var turtle = new Turtle {Generator = generator.Object, Angle = 90};

            // We need to turn then draw to actually get something from the generator.
            turtle.Generate("", "", new List<TurtleCommand>
            {
                TurtleCommand.TurnRight, TurtleCommand.Draw, // (1, 0, 0)
                TurtleCommand.TurnRight, TurtleCommand.Draw, // (1, -1, 0)
                TurtleCommand.TurnRight, TurtleCommand.Draw, // (0, -1, 0)
                TurtleCommand.TurnRight, TurtleCommand.Move, // (0, 0, 0) // no draw
                TurtleCommand.TurnLeft, TurtleCommand.Draw,  // (-1, 0, 0)
                TurtleCommand.TurnLeft, TurtleCommand.Draw,  // (-1, -1, 0)
                TurtleCommand.TurnLeft, TurtleCommand.Draw,  // (0, -1, 0)
            });

            generator.Verify(r => r.DrawSegment(It.IsAny<Vector3>(), It.IsAny<Vector3>()), Times.Exactly(6));
            generator.Verify(r => r.DrawSegment(It.Is<Vector3>(v => v == new Vector3( 0,  0, 0)), It.Is<Vector3>(v => v == new Vector3( 1,  0, 0))), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Vector3>(v => v == new Vector3( 1,  0, 0)), It.Is<Vector3>(v => v == new Vector3( 1, -1, 0))), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Vector3>(v => v == new Vector3( 1, -1, 0)), It.Is<Vector3>(v => v == new Vector3( 0, -1, 0))), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Vector3>(v => v == new Vector3( 0,  0, 0)), It.Is<Vector3>(v => v == new Vector3(-1,  0, 0))), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Vector3>(v => v == new Vector3(-1,  0, 0)), It.Is<Vector3>(v => v == new Vector3(-1, -1, 0))), Times.Once);
            generator.Verify(r => r.DrawSegment(It.Is<Vector3>(v => v == new Vector3(-1, -1, 0)), It.Is<Vector3>(v => v == new Vector3( 0, -1, 0))), Times.Once);
        }
    }
}
