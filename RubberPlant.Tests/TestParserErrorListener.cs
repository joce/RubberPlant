using Antlr4.Runtime;

namespace RubberPlant.Tests
{
    class TestParserErrorListener : LSystemErrorListener
    {
        public int VisitErrorCount { get; private set; }
        public int VisitWarningCount { get; private set; }
        public int VisitInfoCount { get; private set; }
        public int SyntaxErrorCount { get; private set; }

        public TestParserErrorListener()
        {
            VisitErrorCount = 0;
            VisitWarningCount = 0;
            VisitInfoCount = 0;
            SyntaxErrorCount = 0;
        }

        public override void VisitError(ParserRuleContext ctx, ErrorLevel level, string msg)
        {
            switch (level)
            {
                case ErrorLevel.Info:
                    VisitInfoCount++;
                    InfoCount++;
                    break;

                case ErrorLevel.Warning:
                    VisitWarningCount++;
                    WarningCount++;
                    break;

                case ErrorLevel.Error:
                    VisitErrorCount++;
                    ErrorCount++;
                    break;
            }
        }

        public override void SyntaxError(IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
        {
            SyntaxErrorCount++;
            ErrorCount++;
        }

        // For the lexer.
        public override void SyntaxError(IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
        {
            SyntaxErrorCount++;
            ErrorCount++;
        }
    }
}
