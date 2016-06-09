using System.Collections.Generic;
using Moq;
using NUnit.Framework;

namespace RubberPlant.Tests
{
    [TestFixture]
    class LSystemTests
    {
        private Rule m_ruleF;
        private Rule m_ruleA;
        private Rule m_ruleB;

        [SetUp]
        public void SetUp()
        {
            m_ruleF = new Rule();
            m_ruleF.Descriptor.RuleID = 'F';

            m_ruleA = new Rule();
            m_ruleA.Descriptor.RuleID = 'A';

            m_ruleB = new Rule();
            m_ruleB.Descriptor.RuleID = 'B';
        }

        [Test]
        public void LSystemDoesSimpleOneLevelOneRuleReplacement()
        {
            var axiom = new Rule();
            axiom.AddReplacement("F+F+F".ToAtoms());

            m_ruleF.AddReplacement("FFF".ToAtoms());

            LSystem lsys = new LSystem
            {
                Axiom = axiom,
                Rules = new List<Rule> {m_ruleF},
                Vocabulary = {['F'] = TurtleCommand.Draw}
            };

            List<TurtleCommand> result = lsys.ReplaceAndTranslate(1);
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
            var axiom = new Rule();
            axiom.AddReplacement("F+F+F".ToAtoms());

            m_ruleF.AddReplacement("F-f".ToAtoms());

            LSystem lsys = new LSystem
            {
                Axiom = axiom,
                Rules = new List<Rule> {m_ruleF},
                Vocabulary =
                {
                    ['F'] = TurtleCommand.Draw,
                    ['f'] = TurtleCommand.Move
                }
            };

            List<TurtleCommand> result = lsys.ReplaceAndTranslate(1);
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
            var axiom = new Rule();
            axiom.AddReplacement("F+A+B".ToAtoms());

            m_ruleF.AddReplacement("F-f".ToAtoms());
            m_ruleA.AddReplacement("AB".ToAtoms());
            m_ruleB.AddReplacement("FfF".ToAtoms());

            LSystem lsys = new LSystem
            {
                Axiom = axiom,
                Rules = new List<Rule> {m_ruleF, m_ruleA, m_ruleB},
                Vocabulary =
                {
                    ['F'] = TurtleCommand.Draw,
                    ['f'] = TurtleCommand.Move,
                    ['A'] = TurtleCommand.Nop,
                    ['B'] = TurtleCommand.Draw
                }
            };

            List<TurtleCommand> result = lsys.ReplaceAndTranslate(1);
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
            var axiom = new Rule();
            axiom.AddReplacement("F+F+F".ToAtoms());

            m_ruleF.AddReplacement("FFF".ToAtoms());

            LSystem lsys = new LSystem
            {
                Axiom = axiom,
                Rules = new List<Rule> {m_ruleF},
                Vocabulary = {['F'] = TurtleCommand.Draw}
            };

            List<TurtleCommand> result = lsys.ReplaceAndTranslate(2);
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
            var axiom = new Rule();
            axiom.AddReplacement("F+F+F".ToAtoms());

            m_ruleF.AddReplacement("F-f".ToAtoms());

            LSystem lsys = new LSystem
            {
                Axiom = axiom,
                Rules = new List<Rule> {m_ruleF},
                Vocabulary =
                {
                    ['F'] = TurtleCommand.Draw,
                    ['f'] = TurtleCommand.Move
                }
            };

            List<TurtleCommand> result = lsys.ReplaceAndTranslate(2);
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
            var axiom = new Rule();
            axiom.AddReplacement("F+A+B".ToAtoms());

            m_ruleF.AddReplacement("F-f".ToAtoms());
            m_ruleA.AddReplacement("AB".ToAtoms());
            m_ruleB.AddReplacement("FfF".ToAtoms());

            LSystem lsys = new LSystem
            {
                Axiom = axiom,
                Rules = new List<Rule> {m_ruleF, m_ruleA, m_ruleB},
                Vocabulary =
                {
                    ['F'] = TurtleCommand.Draw,
                    ['f'] = TurtleCommand.Move,
                    ['B'] = TurtleCommand.Draw
                }
            };
            // "A" isn't defined and should be treated as Nop.

            List<TurtleCommand> result = lsys.ReplaceAndTranslate(2);
            var expected = new List<TurtleCommand>()
            {
                TurtleCommand.Draw, TurtleCommand.TurnRight, TurtleCommand.Move, TurtleCommand.TurnRight, TurtleCommand.Move, // F-f-f
                TurtleCommand.TurnLeft,                                           // +
                TurtleCommand.Draw, TurtleCommand.Draw, TurtleCommand.Move, TurtleCommand.Draw,            // BFfF
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
            Rule.Random = random.Object;

            var axiom = new Rule();
            axiom.AddReplacement("F".ToAtoms());

            m_ruleF.AddReplacement("F+F".ToAtoms(), 0.3f);
            m_ruleF.AddReplacement("-F-".ToAtoms(), 0.7f);
            LSystem lsys = new LSystem
            {
                Axiom = axiom,
                Rules = new List<Rule> {m_ruleF},
                Vocabulary =
                {
                    ['F'] = TurtleCommand.Draw,
                },
            };

            List<TurtleCommand> result = lsys.ReplaceAndTranslate(2);

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

        [TestCase(1, "abaaaaaaa")]
        [TestCase(2, "aabaaaaaa")]
        [TestCase(3, "aaabaaaaa")]
        [TestCase(4, "aaaabaaaa")]
        [TestCase(5, "aaaaabaaa")]
        [TestCase(6, "aaaaaabaa")]
        [TestCase(7, "aaaaaaaba")]
        [TestCase(8, "aaaaaaaab")]
        [TestCase(9, "aaaaaaaaa")]
        public void LSystemABOPContextualReplacement(int iterations, string expectedResult)
        {
            // From The Algorithmic Beauty of Plants, p. 31
            var axiom = new Rule();
            axiom.AddReplacement("baaaaaaaa".ToAtoms());

            var ruleA = new Rule();
            ruleA.Descriptor.RuleID = 'a';
            ruleA.Descriptor.PreCondition = "b".ToAtoms();
            ruleA.AddReplacement("b".ToAtoms());

            var ruleB = new Rule();
            ruleB.Descriptor.RuleID = 'b';
            ruleB.AddReplacement("a".ToAtoms());

            LSystem lsys = new LSystem
            {
                Axiom = axiom,
                Rules = new List<Rule> { ruleA, ruleB }
            };

            var res = lsys.Replace(iterations);

            Assert.AreEqual(expectedResult.ToAtoms(), res);
        }
    }
}
