namespace Trading.AI.DailyBriefing;

internal sealed class StructuredOutputException : Exception
{
    public StructuredOutputException(string message)
        : base(message)
    {
    }

    public StructuredOutputException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
