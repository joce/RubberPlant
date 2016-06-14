using System.Collections.Generic;
using Moq;
using NUnit.Framework;

namespace RubberPlant.Tests
{
    [TestFixture]
    class LSystemTests
    {
        private Rule m_axiom;
        private Rule m_ruleF;
        private Rule m_ruleA;
        private Rule m_ruleB;

        private LSystem m_lSystem;

        [SetUp]
        public void SetUp()
        {
            m_axiom = new Rule();

            m_ruleF = new Rule();
            m_ruleF.Predecessor.RuleID = 'F';

            m_ruleA = new Rule();
            m_ruleA.Predecessor.RuleID = 'A';

            m_ruleB = new Rule();
            m_ruleB.Predecessor.RuleID = 'B';

            m_lSystem = new LSystem() {Axiom = m_axiom};
        }

        [Test]
        public void LSystemDoesSimpleOneLevelOneRuleReplacement()
        {
            m_axiom.AddSuccessor("F+F+F".ToAtoms());

            m_ruleF.AddSuccessor("FFF".ToAtoms());

            m_lSystem.Rules = new List<Rule> {m_ruleF};

            List<Atom> result = m_lSystem.Replace(1);
            List<Atom> expected = "FFF + FFF + FFF".ToAtoms();
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void LSystemDoesOneLevelOneRuleReplacement()
        {
            m_axiom.AddSuccessor("F+F+F".ToAtoms());

            m_ruleF.AddSuccessor("F-f".ToAtoms());

            m_lSystem.Rules = new List<Rule> {m_ruleF};

            List<Atom> result = m_lSystem.Replace(1);
            List<Atom> expected = "F-f  +  F-f  +  F-f".ToAtoms();
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void LSystemDoesOneLevelMultiRuleReplacement()
        {
            m_axiom.AddSuccessor("F+A+B".ToAtoms());

            m_ruleF.AddSuccessor("F-f".ToAtoms());
            m_ruleA.AddSuccessor("AB".ToAtoms());
            m_ruleB.AddSuccessor("FfF".ToAtoms());

            m_lSystem.Rules = new List<Rule> {m_ruleF, m_ruleA, m_ruleB};

            List<Atom> result = m_lSystem.Replace(1);
            List<Atom> expected = "F-f + AB + FfF".ToAtoms();
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void LSystemDoesSimpleTwoLevelOneRuleReplacement()
        {
            m_axiom.AddSuccessor("F+F+F".ToAtoms());

            m_ruleF.AddSuccessor("FFF".ToAtoms());

            m_lSystem.Rules = new List<Rule> {m_ruleF};

            List<Atom> result = m_lSystem.Replace(2);
            List<Atom> expected = "FFFFFFFFF + FFFFFFFFF + FFFFFFFFF".ToAtoms();
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void LSystemDoesTwoLevelOneRuleReplacement()
        {
            m_axiom.AddSuccessor("F+F+F".ToAtoms());

            m_ruleF.AddSuccessor("F-f".ToAtoms());

            m_lSystem.Rules = new List<Rule> {m_ruleF};

            List<Atom> result = m_lSystem.Replace(2);
            List<Atom> expected = "F-f-f  +  F-f-f  +  F-f-f".ToAtoms();
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void LSystemDoesTwoLevelMultiRuleReplacement()
        {
            m_axiom.AddSuccessor("F+A+B".ToAtoms());

            m_ruleF.AddSuccessor("F-f".ToAtoms());
            m_ruleA.AddSuccessor("AB".ToAtoms());
            m_ruleB.AddSuccessor("FfF".ToAtoms());

            m_lSystem.Rules = new List<Rule> {m_ruleF, m_ruleA, m_ruleB};

            List<Atom> result = m_lSystem.Replace(2);
            List<Atom> expected = "F-f-f  +  ABFfF  +  F-ffF-f".ToAtoms();
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void LSystemDoesProperReplacementWithStochasticRule()
        {
            Mock<IRandom> random = new Mock<IRandom>();
            random.Setup(r => r.NextDouble()).ReturnsInOrder(0.7, 0.2);
            Rule.Random = random.Object;

            m_axiom.AddSuccessor("F".ToAtoms());

            m_ruleF.AddSuccessor("F+F".ToAtoms(), 0.3f);
            m_ruleF.AddSuccessor("-F-".ToAtoms(), 0.7f);

            m_lSystem.Rules = new List<Rule> {m_ruleF};

            List<Atom> result = m_lSystem.Replace(2);

            // Given the "Random" setup, we'll get the 2nd option the 1st time and the 1st option the 2nd time
            List<Atom> expected = "- F + F -".ToAtoms();
            Assert.AreEqual(expected, result);
        }

        [TestCase("+", TurtleCommand.TurnLeft)]
        [TestCase("%", TurtleCommand.CutOffBranch)]
        [TestCase("^", TurtleCommand.PitchUp)]
        [TestCase("&", TurtleCommand.PitchDown)]
        [TestCase("\\", TurtleCommand.RollLeft)]
        [TestCase("/", TurtleCommand.RollRight)]
        [TestCase("$", TurtleCommand.RotateToVertical)]
        [TestCase("+", TurtleCommand.TurnLeft)]
        [TestCase("-", TurtleCommand.TurnRight)]
        [TestCase("|", TurtleCommand.TurnAround)]
        [TestCase("!", TurtleCommand.DecrementDiameter)]
        [TestCase("[", TurtleCommand.StartBranch)]
        [TestCase("]", TurtleCommand.EndBranch)]
        [TestCase("{", TurtleCommand.StartPoly)]
        [TestCase("}", TurtleCommand.EndPoly)]
        [TestCase("\'", TurtleCommand.IncrementColorIndex)]
        [TestCase("~", TurtleCommand.PredefinedSurface)]
        [TestCase(".", TurtleCommand.RecordVertex)]
        public void LSystemConvertsStandardAtomsToTurtleCommands(string atomValue, TurtleCommand expectedCommand)
        {
            Assert.AreEqual(new List<TurtleCommand> {expectedCommand}, m_lSystem.TranslateToTurtleCommands(atomValue.ToAtoms()));
        }

        [Test]
        public void LSystemConvertsSpecifiedVocabularyToTurtleCommands()
        {
            m_lSystem.Vocabulary['f'] = TurtleCommand.Move;
            m_lSystem.Vocabulary['F'] = TurtleCommand.Draw;

            Assert.AreEqual(
                new List<TurtleCommand> {TurtleCommand.Draw, TurtleCommand.Move, TurtleCommand.Draw, TurtleCommand.Move},
                m_lSystem.TranslateToTurtleCommands("FfFf".ToAtoms()));
        }

        [Test]
        public void LSystemConvertsUnspecifiedVocabularyToNoTurtleCommands()
        {
            m_lSystem.Vocabulary['f'] = TurtleCommand.Move;
            m_lSystem.Vocabulary['F'] = TurtleCommand.Draw;

            Assert.AreEqual(
                new List<TurtleCommand> { TurtleCommand.Draw, TurtleCommand.Move },
                m_lSystem.TranslateToTurtleCommands("FABf".ToAtoms()));
        }

        [Test]
        public void LSystemConvertsAndTranslates()
        {
            m_axiom.AddSuccessor("F+A+B".ToAtoms());

            m_ruleF.AddSuccessor("F-f".ToAtoms());
            m_ruleA.AddSuccessor("AB".ToAtoms());
            m_ruleB.AddSuccessor("FfF".ToAtoms());

            m_lSystem.Rules = new List<Rule> {m_ruleF, m_ruleA, m_ruleB};
            m_lSystem.Vocabulary['F'] = TurtleCommand.Draw;
            m_lSystem.Vocabulary['f'] = TurtleCommand.Move;
            m_lSystem.Vocabulary['B'] = TurtleCommand.Draw;
            // Nothing for 'A'. It should generate a NOP

            List<TurtleCommand> result = m_lSystem.ReplaceAndTranslate(1);
            var expected = new List<TurtleCommand>()
            {
                TurtleCommand.Draw, TurtleCommand.TurnRight, TurtleCommand.Move,  // F-f
                TurtleCommand.TurnLeft,                                           // +
                TurtleCommand.Draw,                                               // AB (but A being NOP, just B)
                TurtleCommand.TurnLeft,                                           // +
                TurtleCommand.Draw, TurtleCommand.Move, TurtleCommand.Draw,       // FfF
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
            m_axiom.AddSuccessor("baaaaaaaa".ToAtoms());

            var ruleA = new Rule();
            ruleA.Predecessor.RuleID = 'a';
            ruleA.Predecessor.PreCondition = "b".ToAtoms();
            ruleA.AddSuccessor("b".ToAtoms());

            var ruleB = new Rule();
            ruleB.Predecessor.RuleID = 'b';
            ruleB.AddSuccessor("a".ToAtoms());

            LSystem lsys = new LSystem
            {
                Axiom = m_axiom,
                Rules = new List<Rule> { ruleA, ruleB }
            };

            var res = lsys.Replace(iterations);

            Assert.AreEqual(expectedResult.ToAtoms(), res);
        }
    }
}
