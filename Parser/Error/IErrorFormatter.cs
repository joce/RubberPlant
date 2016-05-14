namespace RubberPlant
{
    public interface IErrorFormatter
    {
        string FormatError(ErrorLevel errorLevel, string message);
        string FormatError(int line, int column, ErrorLevel errorLevel, string message);
    }
}
