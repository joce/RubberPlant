﻿using System;
using System.Collections.Generic;
using System.IO;
using CommandLine;

namespace RubberPlant.App
{
    class Program
    {
        static void Main(string[] args)
        {
            ParserResult<Options> results = CommandLine.Parser.Default.ParseArguments<Options>(args);
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

            foreach (var system in systems)
            {
                var t = new Turtle
                {
                    Renderer = new SVGRenderer(),
                    Angle = system.Angle,
                    StepLength = options.Length
                };
                t.Render(options.OutputDir, system.Name, system.Replace(options.Replacement));
            }
        }
    }
}