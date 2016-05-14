using System.Collections.Generic;
using System.IO;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace RubberPlant
{
    partial class LSystemParser
    {
        public static List<LSystem> ParseStream(Stream inputStream, LSystemErrorListener errorListener)
        {
            AntlrInputStream input = new AntlrInputStream(inputStream);
            LSystemLexer lexer = new LSystemLexer(input);

            lexer.RemoveErrorListeners();
            lexer.AddErrorListener(errorListener);

            CommonTokenStream tokens = new CommonTokenStream(lexer);
            LSystemParser parser = new LSystemParser(tokens);

            parser.RemoveErrorListeners();
            parser.AddErrorListener(errorListener);

            IParseTree tree = parser.lSystemDefinitions();
            if (!errorListener.HasAnySerious)
            {
                LSystemVisitor visitor = new LSystemVisitor(errorListener);
                visitor.Visit(tree);
                return visitor.AllLSystems;
            }

            return new List<LSystem>();
        }
    }
}
