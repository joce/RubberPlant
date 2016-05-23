using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace RubberPlant.Tests
{
    [TestFixture]
    public class ParserTests
    {
        private TestParserErrorListener m_errorListener;

        [TestCase("SimpleTest.ls", 1)]
        [TestCase("MultipleDefinitions.ls", 2)]
        public void ValidFilesLoadProperly(string testName, int lSystemCount)
        {
            var resourceName = "RubberPlant.Tests.ValidTestFiles." + testName;

            IList<LSystem> systems = LoadFromResource(resourceName);
            Assert.AreEqual(lSystemCount, systems.Count);

            AssertErrors();
        }

        [TestCase("MultipleVocabularyBlocks.ls", 3)]
        [TestCase("MultiActionOneLine.ls", 4)]
        public void ActionsCanBeDefinedInMultipleWays(string testName, int vocabularyCount)
        {
            var resourceName = "RubberPlant.Tests.ValidTestFiles." + testName;

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors();
            Assert.AreEqual(1, systems.Count);
            Assert.AreEqual(vocabularyCount, systems[0].Vocabulary.Count);
        }

        // Syntax errors
        [TestCase("UnnamedLSystem.ls", 0, 1)]
        [TestCase("AxiomDefinedOutsideRules.ls", 0, 1)]
        [TestCase("LexerError.ls", 0, 3)]
        // Sementic errors
        [TestCase("DuplicateAction.ls", 1, 0)]
        [TestCase("DuplicateActionInMultipleBlocks.ls", 1, 0)]
        [TestCase("DuplicateActionOnSingleLine.ls", 1, 0)]
        [TestCase("DuplicateActionOnSingleLineAndOther.ls", 1, 0)]
        [TestCase("DuplicateAngle.ls", 1, 0)]
        [TestCase("DuplicateAxiom.ls", 1, 0)]
        [TestCase("DuplicateRule.ls", 1, 0)]
        [TestCase("DuplicateRuleInMultipleBlocks.ls", 1, 0)]
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
        public void RulesDefinedInMultipleBlocksAreOK()
        {
            var resourceName = "RubberPlant.Tests.ValidTestFiles.MultipleRuleBlocks.ls";

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors();
            Assert.AreEqual(1, systems.Count);
            Assert.AreEqual(3, systems[0].Rules.Count);
        }

        // TODO
        [Test]
        public void RuleWithoutDefinedActionGeneratesWarning()
        {
            var resourceName = "RubberPlant.Tests.WarningTestFiles.RuleWithoutAction.ls";

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors(warningCount: 1);
            Assert.AreEqual(1, systems.Count);
            Assert.AreEqual(2, systems[0].Vocabulary.Count);
        }

        [Test]
        [Ignore("Info: Rule isn't used anywhere")]
        public void ExtraneousRuleGeneratesInfo()
        {
        }

        [Test]
        [Ignore("Info: Rule is used only within itself")]
        public void RuleUsedJustWithinItselfGeneratesInfo()
        {
        }

        [Test]
        public void RuleCanStartWithCurlyBracesAndNotBeMistakenForStochasticRule()
        {
            var resourceName = "RubberPlant.Tests.ValidTestFiles.CurlyBracesRule.ls";

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors();
            Assert.AreEqual(1, systems.Count);
            Assert.AreEqual(new List<Atom> {'{', 'F', '-', 'F', '}'}, systems[0].Rules['F']);
        }

        [Test]
        public void StochasticRulesLoadFine()
        {
            var resourceName = "RubberPlant.Tests.ValidTestFiles.StochasticRule.ls";

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors();
            Assert.AreEqual(1, systems.Count);
            Assert.AreEqual(0, systems[0].Rules.Count);
            Assert.AreEqual(1, systems[0].StochasticRules.Count);
            Assert.AreEqual(2, systems[0].StochasticRules['F'].Count);
            Assert.That(0.3, Is.EqualTo(systems[0].StochasticRules['F'][0].Item1).Within(0.00001));
            Assert.That(0.7, Is.EqualTo(systems[0].StochasticRules['F'][1].Item1).Within(0.00001));
        }

        [Test]
        public void StochasticRulesAddingToLessThanOneGenerateWarningAndAreNormalized()
        {
            var resourceName = "RubberPlant.Tests.WarningTestFiles.StochasticRuleLessThanOne.ls";

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors(warningCount: 1);
            Assert.AreEqual(1, systems.Count);
            Assert.AreEqual(0.5, systems[0].StochasticRules['F'][0].Item1);
            Assert.AreEqual(0.5, systems[0].StochasticRules['F'][1].Item1);
        }

        [Test]
        public void StochasticRulesAddingToMoreThanOneGenerateWarningAndAreNormalized()
        {
            var resourceName = "RubberPlant.Tests.WarningTestFiles.StochasticRuleMoreThanOne.ls";

            IList<LSystem> systems = LoadFromResource(resourceName);
            AssertErrors(warningCount: 1);
            Assert.AreEqual(1, systems.Count);
            Assert.That(0.33333, Is.EqualTo(systems[0].StochasticRules['F'][0].Item1).Within(0.00001));
            Assert.That(0.33333, Is.EqualTo(systems[0].StochasticRules['F'][1].Item1).Within(0.00001));
            Assert.That(0.33333, Is.EqualTo(systems[0].StochasticRules['F'][2].Item1).Within(0.00001));
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
