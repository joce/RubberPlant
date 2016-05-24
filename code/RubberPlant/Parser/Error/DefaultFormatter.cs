namespace RubberPlant
{
    public class DefaultErrorFormatter : IErrorFormatter
    {
        public string FormatError(ErrorLevel errorLevel, string message)
        {
            return FormatError(-1, -1, errorLevel, message);
        }

        public string FormatError(int line, int column, ErrorLevel errorLevel, string message)
        {
            if (line < 0 || column < 0)
            {
                return string.Format("{0}: {1}", errorLevel, message);
            }

            return string.Format("({0}, {1}) {2}: {3}", line, column, errorLevel, message);
        }
    }
}
