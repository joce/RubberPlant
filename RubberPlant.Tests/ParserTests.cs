using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace RubberPlant.Tests
{
    [TestFixture]
    public class ParserTests
    {
        [SetUp]
        public void Setup()
        { }

        [TearDown]
        public void TearDown()
        {
        }

        [TestCase("SimpleTest.ls", 1)]
        [TestCase("MultipleDefinitions.ls", 2)]
        public void ValidFilesLoadProperly(string testName, int lSystemCount)
        {
            var resourceName = "RubberPlant.Tests.ValidTestFiles." + testName;
            var errorListner = new TestParserErrorListener();

            IList<LSystem> systems = LoadFromResource(resourceName, errorListner);
            Assert.AreEqual(lSystemCount, systems.Count);

            Assert.AreEqual(0, errorListner.VisitInfoCount);
            Assert.AreEqual(0, errorListner.VisitWarningCount);
            Assert.AreEqual(0, errorListner.VisitErrorCount);
            Assert.AreEqual(0, errorListner.SyntaxErrorCount);
        }

        [TestCase("MultipleVocabularyBlocks.ls", 3)]
        [TestCase("MultiActionOneLine.ls", 4)]
        public void ActionsCanBeDefinedInMultipleWays(string testName, int vocabularyCount)
        {
            var resourceName = "RubberPlant.Tests.ValidTestFiles." + testName;
            var errorListner = new TestParserErrorListener();

            IList<LSystem> systems = LoadFromResource(resourceName, errorListner);
            Assert.AreEqual(1, systems.Count);

            Assert.AreEqual(vocabularyCount, systems[0].Vocabulary.Count);

            Assert.AreEqual(0, errorListner.VisitInfoCount);
            Assert.AreEqual(0, errorListner.VisitWarningCount);
            Assert.AreEqual(0, errorListner.VisitErrorCount);
            Assert.AreEqual(0, errorListner.SyntaxErrorCount);
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
            var errorListner = new TestParserErrorListener();

            IList<LSystem> systems = LoadFromResource(resourceName, errorListner);
            Assert.AreEqual(0, systems.Count);

            Assert.AreEqual(0, errorListner.VisitInfoCount);
            Assert.AreEqual(0, errorListner.VisitWarningCount);
            Assert.AreEqual(visitErrorCount, errorListner.VisitErrorCount);
            Assert.AreEqual(syntaxErrorCount, errorListner.SyntaxErrorCount);
        }

        [Test]
        public void LSystemWithMissingAngleLoadsWithWarningAndDefaultsTo90Degres()
        {
            var resourceName = "RubberPlant.Tests.ErrorTestFiles.MissingAngle.ls";
            var errorListner = new TestParserErrorListener();

            IList<LSystem> systems = LoadFromResource(resourceName, errorListner);
            Assert.AreEqual(1, systems.Count);
            Assert.AreEqual(90, systems[0].Angle);

            Assert.AreEqual(0, errorListner.VisitInfoCount);
            Assert.AreEqual(1, errorListner.VisitWarningCount);
            Assert.AreEqual(0, errorListner.VisitErrorCount);
            Assert.AreEqual(0, errorListner.SyntaxErrorCount);
        }

        [Test]
        public void RulesDefinedInMultipleBlocksAreOK()
        {
            var resourceName = "RubberPlant.Tests.ValidTestFiles.MultipleRuleBlocks.ls";
            var errorListner = new TestParserErrorListener();

            IList<LSystem> systems = LoadFromResource(resourceName, errorListner);
            Assert.AreEqual(1, systems.Count);

            Assert.AreEqual(3, systems[0].Rules.Count);

            Assert.AreEqual(0, errorListner.VisitInfoCount);
            Assert.AreEqual(0, errorListner.VisitWarningCount);
            Assert.AreEqual(0, errorListner.VisitErrorCount);
            Assert.AreEqual(0, errorListner.SyntaxErrorCount);
        }

        // TODO
        [Test]
        [Ignore("Warning: Defaults to NOP")]
        public void RuleWithoutDefinedActionGeneratesWarning()
        {
            var resourceName = "RubberPlant.Tests.ErrorTestFiles.RulesWithoutAction.ls";
            var errorListner = new TestParserErrorListener();

            IList<LSystem> systems = LoadFromResource(resourceName, errorListner);
            Assert.AreEqual(1, systems.Count);

            Assert.AreEqual(2, systems[0].Vocabulary.Count);

            Assert.AreEqual(0, errorListner.VisitInfoCount);
            Assert.AreEqual(1, errorListner.VisitWarningCount);
            Assert.AreEqual(0, errorListner.VisitErrorCount);
            Assert.AreEqual(0, errorListner.SyntaxErrorCount);
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
            var errorListner = new TestParserErrorListener();

            IList<LSystem> systems = LoadFromResource(resourceName, errorListner);
            Assert.AreEqual(1, systems.Count);

            Assert.AreEqual(0, errorListner.VisitInfoCount);
            Assert.AreEqual(0, errorListner.VisitWarningCount);
            Assert.AreEqual(0, errorListner.VisitErrorCount);
            Assert.AreEqual(0, errorListner.SyntaxErrorCount);

            Assert.AreEqual(new List<Atom> {'{', 'F', '-', 'F', '}'}, systems[0].Rules['F']);
        }

        [Test]
        public void StochasticRulesLoadFine()
        {
            var resourceName = "RubberPlant.Tests.ValidTestFiles.StochasticRule.ls";
            var errorListner = new TestParserErrorListener();

            IList<LSystem> systems = LoadFromResource(resourceName, errorListner);
            Assert.AreEqual(1, systems.Count);

            Assert.AreEqual(0, errorListner.VisitInfoCount);
            Assert.AreEqual(0, errorListner.VisitWarningCount);
            Assert.AreEqual(0, errorListner.VisitErrorCount);
            Assert.AreEqual(0, errorListner.SyntaxErrorCount);

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
            var errorListner = new TestParserErrorListener();

            IList<LSystem> systems = LoadFromResource(resourceName, errorListner);
            Assert.AreEqual(1, systems.Count);

            Assert.AreEqual(0, errorListner.VisitInfoCount);
            Assert.AreEqual(1, errorListner.VisitWarningCount);
            Assert.AreEqual(0, errorListner.VisitErrorCount);
            Assert.AreEqual(0, errorListner.SyntaxErrorCount);

            Assert.AreEqual(0.5, systems[0].StochasticRules['F'][0].Item1);
            Assert.AreEqual(0.5, systems[0].StochasticRules['F'][1].Item1);
        }

        [Test]
        public void StochasticRulesAddingToMoreThanOneGenerateWarningAndAreNormalized()
        {
            var resourceName = "RubberPlant.Tests.WarningTestFiles.StochasticRuleMoreThanOne.ls";
            var errorListner = new TestParserErrorListener();

            IList<LSystem> systems = LoadFromResource(resourceName, errorListner);
            Assert.AreEqual(1, systems.Count);

            Assert.AreEqual(0, errorListner.VisitInfoCount);
            Assert.AreEqual(1, errorListner.VisitWarningCount);
            Assert.AreEqual(0, errorListner.VisitErrorCount);
            Assert.AreEqual(0, errorListner.SyntaxErrorCount);

            Assert.That(0.33333, Is.EqualTo(systems[0].StochasticRules['F'][0].Item1).Within(0.00001));
            Assert.That(0.33333, Is.EqualTo(systems[0].StochasticRules['F'][1].Item1).Within(0.00001));
            Assert.That(0.33333, Is.EqualTo(systems[0].StochasticRules['F'][2].Item1).Within(0.00001));
        }

        private IList<LSystem> LoadFromResource(string resourceName, LSystemErrorListener errorListener)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            Stream resourceStream = assembly.GetManifestResourceStream(resourceName);

            return LSystemParser.ParseStream(resourceStream, errorListener);
        }
    }
}
