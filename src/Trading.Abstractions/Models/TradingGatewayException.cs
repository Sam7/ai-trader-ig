namespace Trading.Abstractions;

public sealed class TradingGatewayException : Exception
{
    public TradingGatewayException(
        TradingErrorCode errorCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public TradingErrorCode ErrorCode { get; }
}
