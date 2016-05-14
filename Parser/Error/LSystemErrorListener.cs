using System;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Dfa;
using Antlr4.Runtime.Sharpen;

namespace RubberPlant
{
    public class LSystemErrorListener : BaseErrorListener, IAntlrErrorListener<int>
    {
        private IErrorFormatter m_errorFormatter;
        private IErrorOutput m_errorOutput;

        public IErrorFormatter ErrorFormatter
        {
            get { return m_errorFormatter ?? (m_errorFormatter = new DefaultErrorFormatter()); }
            set { m_errorFormatter = value; }
        }

        public IErrorOutput ErrorOutput
        {
            get { return m_errorOutput ?? (m_errorOutput = new ConsoleErrorOutput()); }
            set { m_errorOutput = value; }
        }

        public int ErrorCount { get; protected set; }
        public int WarningCount { get; protected set; }
        public int InfoCount { get; protected set; }

        public bool HasErrors => ErrorCount > 0;
        public bool HasWarnings => WarningCount > 0;
        public bool HasInfo => InfoCount > 0;
        public bool HasAnySerious => HasErrors || HasWarnings;
        public bool HasAny => HasErrors || HasWarnings || HasInfo;

        public LSystemErrorListener()
        {
            ErrorCount = 0;
            WarningCount = 0;
            InfoCount = 0;
        }

        // TODO: we might want to have number IDs for the different errors we know we can have.

        public virtual void VisitError(ParserRuleContext ctx, ErrorLevel level, string msg)
        {
            switch (level)
            {
                case ErrorLevel.Info:
                    InfoCount++;
                    break;
                case ErrorLevel.Warning:
                    WarningCount++;
                    break;
                case ErrorLevel.Error:
                    ErrorCount++;
                    break;
            }
            ErrorOutput.OutputError(ErrorFormatter.FormatError(ctx.Start.Line, ctx.Start.Column, level, msg));
        }

        public override void ReportAmbiguity(Parser recognizer, DFA dfa, int startIndex, int stopIndex, bool exact, BitSet ambigAlts, ATNConfigSet configs)
        {
            ErrorCount++;
            throw new NotImplementedException("Don't know how to report ambiguity.");
        }

        public override void ReportAttemptingFullContext(Parser recognizer, DFA dfa, int startIndex, int stopIndex, BitSet conflictingAlts, SimulatorState conflictState)
        {
            ErrorCount++;
            throw new NotImplementedException("Don't know how to report attempting full context.");
        }

        public override void ReportContextSensitivity(Parser recognizer, DFA dfa, int startIndex, int stopIndex, int prediction, SimulatorState acceptState)
        {
            ErrorCount++;
            throw new NotImplementedException("Don't know how to report context sensitivity.");
        }

        public override void SyntaxError(IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
        {
            ErrorCount++;
            ErrorOutput.OutputError(ErrorFormatter.FormatError(line, charPositionInLine, ErrorLevel.Error, msg));
        }

        // For the lexer.
        public virtual void SyntaxError(IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
        {
            ErrorCount++;
            ErrorOutput.OutputError(ErrorFormatter.FormatError(line, charPositionInLine, ErrorLevel.Error, msg));
        }
    }
}
