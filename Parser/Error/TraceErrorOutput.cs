using System.Diagnostics;

namespace RubberPlant
{
    public class TraceErrorOutput : IErrorOutput
    {
        public void OutputError(string error)
        {
            Trace.WriteLine(error);
        }
    }
}
