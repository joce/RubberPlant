using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;

namespace RubberPlant.Tests
{
    [TestFixture]
    class LSystemTests
    {
        [Test]
        public void LSystemDoesSimpleOneLevelOneRuleReplacement()
        {
            LSystem lsys = new LSystem
            {
                Axiom = new List<Atom> {'F', '+', 'F', '+', 'F'},
                Rules = {['F'] = new List<Atom> {'F', 'F', 'F'}},
                Vocabulary = {['F'] = TurtleCommand.Draw}
            };

            List<TurtleCommand> result = lsys.Replace(1);
            var expected = new List<TurtleCommand>()
            {
                TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw,  // FFF
                TurtleCommand.TurnLeft,                                      // +
                TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw,  // FFF
                TurtleCommand.TurnLeft,                                      // +
                TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw,  // FFF
            };
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void LSystemDoesOneLevelOneRuleReplacement()
        {
            LSystem lsys = new LSystem
            {
                Axiom = new List<Atom> {'F', '+', 'F', '+', 'F'},
                Rules = {['F'] = new List<Atom> {'F', '-', 'f'}},
                Vocabulary =
                {
                    ['F'] = TurtleCommand.Draw,
                    ['f'] = TurtleCommand.Move
                }
            };

            List<TurtleCommand> result = lsys.Replace(1);
            var expected = new List<TurtleCommand>()
            {
                TurtleCommand.Draw, TurtleCommand.TurnRight, TurtleCommand.Move,  // F-f
                TurtleCommand.TurnLeft,                                           // +
                TurtleCommand.Draw, TurtleCommand.TurnRight, TurtleCommand.Move,  // F-f
                TurtleCommand.TurnLeft,                                           // +
                TurtleCommand.Draw, TurtleCommand.TurnRight, TurtleCommand.Move,  // F-f
            };
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void LSystemDoesOneLevelMultiRuleReplacement()
        {
            LSystem lsys = new LSystem
            {
                Axiom = new List<Atom> {'F', '+', 'A', '+', 'B'},
                Rules =
                {
                    ['F'] = new List<Atom> {'F', '-', 'f'},
                    ['A'] = new List<Atom> {'A', 'B'},
                    ['B'] = new List<Atom> {'F', 'f', 'F'}
                },
                Vocabulary =
                {
                    ['F'] = TurtleCommand.Draw,
                    ['f'] = TurtleCommand.Move,
                    ['A'] = TurtleCommand.Nop,
                    ['B'] = TurtleCommand.Draw
                }
            };

            List<TurtleCommand> result = lsys.Replace(1);
            var expected = new List<TurtleCommand>()
            {
                TurtleCommand.Draw, TurtleCommand.TurnRight, TurtleCommand.Move,  // F-f
                TurtleCommand.TurnLeft,                                           // +
                TurtleCommand.Nop, TurtleCommand.Draw,                            // AB
                TurtleCommand.TurnLeft,                                           // +
                TurtleCommand.Draw, TurtleCommand.Move, TurtleCommand.Draw,       // FfF
            };
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void LSystemDoesSimpleTwoLevelOneRuleReplacement()
        {
            LSystem lsys = new LSystem
            {
                Axiom = new List<Atom> {'F', '+', 'F', '+', 'F'},
                Rules = {['F'] = new List<Atom> {'F', 'F', 'F'}},
                Vocabulary = {['F'] = TurtleCommand.Draw}
            };

            List<TurtleCommand> result = lsys.Replace(2);
            var expected = new List<TurtleCommand>()
            {
                TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, // FFFFFFFFF
                TurtleCommand.TurnLeft,                                                                                                                                                             // +
                TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, // FFFFFFFFF
                TurtleCommand.TurnLeft,                                                                                                                                                             // +
                TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Draw, // FFFFFFFFF
            };
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void LSystemDoesTwoLevelOneRuleReplacement()
        {
            LSystem lsys = new LSystem
            {
                Axiom = new List<Atom> {'F', '+', 'F', '+', 'F'},
                Rules = {['F'] = new List<Atom> {'F', '-', 'f'}},
                Vocabulary =
                {
                    ['F'] = TurtleCommand.Draw,
                    ['f'] = TurtleCommand.Move
                }
            };

            List<TurtleCommand> result = lsys.Replace(2);
            var expected = new List<TurtleCommand>()
            {
                TurtleCommand.Draw, TurtleCommand.TurnRight, TurtleCommand.Move, TurtleCommand.TurnRight, TurtleCommand.Move, // F-f-f
                TurtleCommand.TurnLeft,                                           // +
                TurtleCommand.Draw, TurtleCommand.TurnRight, TurtleCommand.Move, TurtleCommand.TurnRight, TurtleCommand.Move, // F-f-f
                TurtleCommand.TurnLeft,                                           // +
                TurtleCommand.Draw, TurtleCommand.TurnRight, TurtleCommand.Move, TurtleCommand.TurnRight, TurtleCommand.Move, // F-f-f
            };
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void LSystemDoesTwoLevelMultiRuleReplacement()
        {
            LSystem lsys = new LSystem
            {
                Axiom = new List<Atom> {'F', '+', 'A', '+', 'B'},
                Rules =
                {
                    ['F'] = new List<Atom> {'F', '-', 'f'},
                    ['A'] = new List<Atom> {'A', 'B'},
                    ['B'] = new List<Atom> {'F', 'f', 'F'}
                },
                Vocabulary =
                {
                    ['F'] = TurtleCommand.Draw,
                    ['f'] = TurtleCommand.Move,
                    ['B'] = TurtleCommand.Draw
                }
            };
            // "A" isn't defined and should be treated as Nop.

            List<TurtleCommand> result = lsys.Replace(2);
            var expected = new List<TurtleCommand>()
            {
                TurtleCommand.Draw, TurtleCommand.TurnRight, TurtleCommand.Move, TurtleCommand.TurnRight, TurtleCommand.Move, // F-f-f
                TurtleCommand.TurnLeft,                                           // +
                TurtleCommand.Nop, TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Move, TurtleCommand.Draw,            // ABFfF
                TurtleCommand.TurnLeft,                                           // +
                TurtleCommand.Draw, TurtleCommand.TurnRight, TurtleCommand.Move, TurtleCommand.Move, TurtleCommand.Draw, TurtleCommand.TurnRight, TurtleCommand.Move, // F-ffF-f
            };
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void LSystemDoesProperReplacementWithStochasticRule()
        {
            Mock<IRandom> random = new Mock<IRandom>();
            random.Setup(r => r.NextDouble()).ReturnsInOrder(0.7, 0.2);
            LSystem lsys = new LSystem
            {
                Axiom = new List<Atom> { 'F' },
                Random = random.Object,
                StochasticRules =
                {
                    ['F'] = new List<Tuple<float, List<Atom>>>
                    {
                        new Tuple<float, List<Atom>>(0.3f, new List<Atom> {'F', '+', 'F'}),
                        new Tuple<float, List<Atom>>(0.7f, new List<Atom> {'-', 'F', '-'})
                    }
                },
                Vocabulary =
                {
                    ['F'] = TurtleCommand.Draw,
                },
            };

            List<TurtleCommand> result = lsys.Replace(2);

            // Given the "Random" setup, we'll get the 2nd option the 1st time and the 1st option the 2nd time
            // - F + F -
            var expected = new List<TurtleCommand>
            {
                TurtleCommand.TurnRight, TurtleCommand.Draw,
                TurtleCommand.TurnLeft,
                TurtleCommand.Draw, TurtleCommand.TurnRight,
            };
            Assert.AreEqual(expected, result);
        }
    }
}
