namespace Ig.Trading.Sdk.Streaming;

public sealed class IgStreamingDataException : Exception
{
    public IgStreamingDataException(string message)
        : base(message)
    {
    }
}
