using CommandLine;

namespace RubberPlant.App
{
    class Options
    {
        [Option('r', "replace", Required = true, HelpText = "Number of replacement to do.")]
        public int Replacement { get; set; }

        [Option('l', "length", Required = false, HelpText = "Base segment length", Default = 1.0f)]
        public float Length { get; set; }

        [Option('o', "output", Required = false, HelpText = "(Default: same as input) Output directory")]
        public string OutputDir { get; set; }

        [Value(0, MetaName = "input file",
            HelpText = "Input file to be processed.",
            Required = true)]
        public string FileName { get; set; }
    }
}
