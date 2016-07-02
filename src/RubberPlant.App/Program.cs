using System;
using System.Collections.Generic;
using System.IO;
using CommandLine;

namespace RubberPlant.App
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            ParserResult<Options> results = Parser.Default.ParseArguments<Options>(args);
            if (results.Tag == ParserResultType.NotParsed)
            {
                return;
            }

            Options options = ((Parsed<Options>)results).Value;

            if (!File.Exists(options.FileName))
            {
                Console.WriteLine("File {0} does not exists", options.FileName);
                return;
            }

            if (string.IsNullOrEmpty(options.OutputDir))
            {
                options.OutputDir = Path.GetDirectoryName(options.FileName);
            }

            List<LSystem> systems;
            using (FileStream fs = new FileStream(options.FileName, FileMode.Open))
            {
                var errorListener = new LSystemErrorListener();
                systems = LSystemParser.ParseStream(fs, errorListener);
            }

            if (options.ForcedSeed != null)
            {
                Rule.Random = new Random(options.ForcedSeed.Value);
            }

            foreach (var system in systems)
            {
                var t = new Turtle
                {
                    Generator = new SVGGenerator(),
                    Angle = system.Angle,
                    StepLength = options.Length
                };
                t.Generate(options.OutputDir, system.Name, system.ReplaceAndTranslate(options.Replacement));
            }
        }
    }
}
