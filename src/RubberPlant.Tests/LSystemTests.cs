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
            var axiom = new Rule();
            axiom.AddBody(new List<Atom> {'F', '+', 'F', '+', 'F'});

            var rule = new Rule {RuleID = 'F'};
            rule.AddBody(new List<Atom> {'F', 'F', 'F'});

            LSystem lsys = new LSystem
            {
                Axiom = axiom,
                Rules = new List<Rule> {rule},
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
            var axiom = new Rule();
            axiom.AddBody(new List<Atom> {'F', '+', 'F', '+', 'F'});

            var rule = new Rule {RuleID = 'F'};
            rule.AddBody(new List<Atom> {'F', '-', 'f'});

            LSystem lsys = new LSystem
            {
                Axiom = axiom,
                Rules = new List<Rule> {rule},
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
            var axiom = new Rule();
            axiom.AddBody(new List<Atom> {'F', '+', 'A', '+', 'B'});

            var ruleF = new Rule {RuleID = 'F'};
            ruleF.AddBody(new List<Atom> {'F', '-', 'f'});

            var ruleA = new Rule {RuleID = 'A'};
            ruleA.AddBody(new List<Atom> {'A', 'B'});

            var ruleB = new Rule {RuleID = 'B', };
            ruleB.AddBody(new List<Atom> {'F', 'f', 'F'});

            LSystem lsys = new LSystem
            {
                Axiom = axiom,
                Rules = new List<Rule> {ruleF, ruleA, ruleB},
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
            var axiom = new Rule();
            axiom.AddBody(new List<Atom> { 'F', '+', 'F', '+', 'F' });

            var rule = new Rule { RuleID = 'F' };
            rule.AddBody(new List<Atom> {'F', 'F', 'F'});

            LSystem lsys = new LSystem
            {
                Axiom = axiom,
                Rules = new List<Rule> {rule},
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
            var axiom = new Rule();
            axiom.AddBody(new List<Atom> { 'F', '+', 'F', '+', 'F' });

            var rule = new Rule { RuleID = 'F' };
            rule.AddBody(new List<Atom> {'F', '-', 'f'});

            LSystem lsys = new LSystem
            {
                Axiom = axiom,
                Rules = new List<Rule> {rule},
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
            var axiom = new Rule();
            axiom.AddBody(new List<Atom> { 'F', '+', 'A', '+', 'B' });

            var ruleF = new Rule {RuleID = 'F'};
            ruleF.AddBody(new List<Atom> {'F', '-', 'f'});

            var ruleA = new Rule {RuleID = 'A'};
            ruleA.AddBody(new List<Atom> {'A', 'B'});

            var ruleB = new Rule {RuleID = 'B', };
            ruleB.AddBody(new List<Atom> {'F', 'f', 'F'});

            LSystem lsys = new LSystem
            {
                Axiom = axiom,
                Rules = new List<Rule> {ruleF, ruleA, ruleB},
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
            Rule.Random = random.Object;

            var axiom = new Rule();
            axiom.AddBody(new List<Atom> { 'F' });

            var rule = new Rule { RuleID = 'F' };
            rule.AddBody(new List<Atom> {'F', '+', 'F'}, 0.3f);
            rule.AddBody(new List<Atom> {'-', 'F', '-'}, 0.7f);
            LSystem lsys = new LSystem
            {
                Axiom = axiom,
                Rules = new List<Rule> {rule},
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
