using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace RubberPlant.Tests
{
    [TestFixture]
    public class ParserTests
    {
        private TestParserErrorListener m_errorListener;
        private static readonly RulePredecessor k_ruleF = new RulePredecessor {RuleID = new Atom('F')};

        [TestCase("SimpleTest.ls", 1)]
        [TestCase("MultipleDefinitions.ls", 2)]
        [TestCase("EmptyBodyRule.ls", 1)]
        [TestCase("TurtleCommandsAsRuleName.ls", 1)]
        public void ValidFilesLoadProperly(string testName, int lSystemCount)
        {
            var resourceName = "RubberPlant.Tests.ValidTestFiles." + testName;

            IList<LSystem> systems = LoadFromResource(resourceName);
            Assert.AreEqual(lSystemCount, systems.Count);

            AssertErrors();
        }

        [TestCase("MultiActionOneLine.ls", 4)]
        public void ActionsCanBeDefinedInMultipleWays(string testName, int vocabularyCount)
        {
            var resourceName = "RubberPlant.Tests.ValidTestFiles." + testName;

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors();
            Assert.AreEqual(1, systems.Count);
            Assert.AreEqual(vocabularyCount + LSystem.k_implicitTurtleCommands.Count, systems[0].Vocabulary.Count);
        }

        // Syntax errors
        [TestCase("UnnamedLSystem.ls", 0, 4)]
        [TestCase("LexerError.ls", 0, 3)]
        // Sementic errors
        [TestCase("DuplicateAction.ls", 1, 0)]
        [TestCase("DuplicateActionOnSingleLine.ls", 1, 0)]
        [TestCase("DuplicateActionOnSingleLineAndOther.ls", 1, 0)]
        [TestCase("DuplicateAngle.ls", 1, 0)]
        [TestCase("DuplicateAxiom.ls", 1, 0)]
        [TestCase("DuplicateRule.ls", 1, 0)]
        [TestCase("IgnoreBranch.ls", 1, 0)]
        [TestCase("MissingAxiom.ls", 1, 0)]
        [TestCase("StochasticNegativeWeight.ls", 1, 0)]
        [TestCase("StochasticZeroWeight.ls", 1, 0)]
        public void ErroneousFilesReportErrorsProperly(string testName, int visitErrorCount, int syntaxErrorCount)
        {
            var resourceName = "RubberPlant.Tests.ErrorTestFiles." + testName;

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors(errorCount: visitErrorCount, syntaxErrorCount: syntaxErrorCount);
            Assert.AreEqual(0, systems.Count);
        }

        [Test]
        public void LSystemWithMissingAngleLoadsWithWarningAndDefaultsTo90Degres()
        {
            var resourceName = "RubberPlant.Tests.WarningTestFiles.MissingAngle.ls";

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors(warningCount: 1);
            Assert.AreEqual(1, systems.Count);
            Assert.AreEqual(90, systems[0].Angle);
        }

        [Test]
        public void RuleWithoutDefinedActionGeneratesWarning()
        {
            var resourceName = "RubberPlant.Tests.WarningTestFiles.RuleWithoutAction.ls";

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors(warningCount: 1);
            Assert.AreEqual(1, systems.Count);
            Assert.AreEqual(2 + LSystem.k_implicitTurtleCommands.Count, systems[0].Vocabulary.Count);
        }

        // TODO
        [Test]
        [Ignore("Info: Rule isn't used anywhere")]
        public void ExtraneousRuleGeneratesInfo()
        {
            throw new NotImplementedException();
        }

        // TODO
        [Test]
        [Ignore("Info: Rule is used only within itself")]
        public void RuleUsedJustWithinItselfGeneratesInfo()
        {
            throw new NotImplementedException();
        }

        [Test]
        public void RuleCanStartWithCurlyBracesAndNotBeMistakenForStochasticRule()
        {
            var resourceName = "RubberPlant.Tests.ValidTestFiles.CurlyBracesRule.ls";

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors();
            Assert.AreEqual(1, systems.Count);
            Assert.AreEqual( "{F-F}".ToAtoms(), systems[0].GetRule(k_ruleF).Successor);
        }

        [Test]
        public void StochasticRulesLoadFine()
        {
            var resourceName = "RubberPlant.Tests.ValidTestFiles.StochasticRule.ls";

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors();
            Assert.AreEqual(1, systems.Count);
            Assert.AreEqual(1, systems[0].Rules.Count);
            Assert.AreEqual(2, systems[0].GetRule(k_ruleF).SuccessorCount);
            Assert.That(0.3, Is.EqualTo(systems[0].GetRule(k_ruleF)[0].Item1).Within(0.00001));
            Assert.That(0.7, Is.EqualTo(systems[0].GetRule(k_ruleF)[1].Item1).Within(0.00001));
        }

        [Test]
        public void StochasticRulesAddingToLessThanOneGenerateWarningAndAreNormalized()
        {
            var resourceName = "RubberPlant.Tests.WarningTestFiles.StochasticRuleLessThanOne.ls";

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors(warningCount: 1);
            Assert.AreEqual(1, systems.Count);
            Assert.AreEqual(0.5, systems[0].GetRule(k_ruleF)[0].Item1);
            Assert.AreEqual(0.5, systems[0].GetRule(k_ruleF)[1].Item1);
        }

        [Test]
        public void StochasticRulesAddingToMoreThanOneGenerateWarningAndAreNormalized()
        {
            var resourceName = "RubberPlant.Tests.WarningTestFiles.StochasticRuleMoreThanOne.ls";

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors(warningCount: 1);
            Assert.AreEqual(1, systems.Count);
            Assert.That(0.33333, Is.EqualTo(systems[0].GetRule(k_ruleF)[0].Item1).Within(0.00001));
            Assert.That(0.33333, Is.EqualTo(systems[0].GetRule(k_ruleF)[1].Item1).Within(0.00001));
            Assert.That(0.33333, Is.EqualTo(systems[0].GetRule(k_ruleF)[2].Item1).Within(0.00001));
        }

        [Test]
        public void RulesWithPreAndPostConditionsLoadProperly()
        {
            var resourceName = "RubberPlant.Tests.ValidTestFiles.PrePostContitions.ls";

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors();
            Assert.AreEqual(1, systems.Count);
            Assert.AreEqual(4, systems[0].Rules.Count);
        }

        [Test]
        public void RulePreConditionsLoadBackwards()
        {
            var resourceName = "RubberPlant.Tests.ValidTestFiles.PrePostContitions.ls";

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors();
            Assert.AreEqual("+ABF".ToAtoms(), systems[0].GetRules('A').First().Predecessor.PreCondition);
        }

        [Test]
        public void RulePostConditionsLoadForwards()
        {
            var resourceName = "RubberPlant.Tests.ValidTestFiles.PrePostContitions.ls";

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors();
            Assert.AreEqual("FABB+".ToAtoms(), systems[0].GetRules('F').First().Predecessor.PostCondition);
        }

        [Test]
        public void RulesWithImpossibleConditionsGenerateWarnings()
        {
            var resourceName = "RubberPlant.Tests.WarningTestFiles.ImpossiblePrePostContitions.ls";

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors(warningCount:1);
            Assert.AreEqual(1, systems.Count);
            Assert.AreEqual(3, systems[0].Rules.Count);
        }

        [Test]
        public void RulesWithDuplicateIgnoreGenerateInfo()
        {
            var resourceName = "RubberPlant.Tests.InfoTestFiles.MultiIgnore.ls";

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors(infoCount: 1);
            Assert.AreEqual(1, systems.Count);
            Assert.AreEqual(4, systems[0].Rules.Count);
            Assert.AreEqual(2, systems[0].MatchIgnores.Count);
        }

        private IList<LSystem> LoadFromResource(string resourceName)
        {
            m_errorListener = new TestParserErrorListener();

            Assembly assembly = Assembly.GetExecutingAssembly();
            Stream resourceStream = assembly.GetManifestResourceStream(resourceName);

            return LSystemParser.ParseStream(resourceStream, m_errorListener);
        }

        private void AssertErrors(int infoCount = 0, int warningCount = 0, int errorCount = 0, int syntaxErrorCount = 0)
        {
            Assert.AreEqual(infoCount, m_errorListener.VisitInfoCount);
            Assert.AreEqual(warningCount, m_errorListener.VisitWarningCount);
            Assert.AreEqual(errorCount, m_errorListener.VisitErrorCount);
            Assert.AreEqual(syntaxErrorCount, m_errorListener.SyntaxErrorCount);
        }
    }
}
