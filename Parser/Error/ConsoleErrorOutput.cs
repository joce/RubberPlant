using System;

namespace RubberPlant
{
    public class ConsoleErrorOutput : IErrorOutput
    {
        public void OutputError(string error)
        {
            Console.WriteLine(error);
        }
    }
}
