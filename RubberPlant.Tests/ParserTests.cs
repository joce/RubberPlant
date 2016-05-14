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

        // Syntax errors
        [TestCase("UnnamedLSystem.ls", 0, 1)]
        [TestCase("AxiomDefinedOutsideRules.ls", 0, 1)]
        [TestCase("LexerError.ls", 0, 3)]
        // Sementic errors
        [TestCase("MissingAxiom.ls", 1, 0)]
        [TestCase("DuplicateAxiom.ls", 1, 0)]
        [TestCase("DuplicateAngle.ls", 1, 0)]
        [TestCase("DuplicateRule.ls", 1, 0)]
        [TestCase("DuplicateRuleInMultipleBlocks.ls", 1, 0)]
        [TestCase("DuplicateAction.ls", 1, 0)]
        [TestCase("DuplicateActionInMultipleBlocks.ls", 1, 0)]
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

        [Test]
        public void ActionsDefinedInMultipleBlocksAreOK()
        {
            var resourceName = "RubberPlant.Tests.ValidTestFiles.MultipleVocabularyBlocks.ls";
            var errorListner = new TestParserErrorListener();

            IList<LSystem> systems = LoadFromResource(resourceName, errorListner);
            Assert.AreEqual(1, systems.Count);

            Assert.AreEqual(3, systems[0].Vocabulary.Count);

            Assert.AreEqual(0, errorListner.VisitInfoCount);
            Assert.AreEqual(0, errorListner.VisitWarningCount);
            Assert.AreEqual(0, errorListner.VisitErrorCount);
            Assert.AreEqual(0, errorListner.SyntaxErrorCount);
        }

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

        private IList<LSystem> LoadFromResource(string resourceName, LSystemErrorListener errorListener)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            Stream resourceStream = assembly.GetManifestResourceStream(resourceName);

            return LSystemParser.ParseStream(resourceStream, errorListener);
        }
    }
}
