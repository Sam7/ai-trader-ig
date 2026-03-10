using System.Net;

namespace Ig.Trading.Sdk.Errors;

public sealed class IgApiException : Exception
{
    public IgApiException(string? errorCode, HttpStatusCode? statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public string? ErrorCode { get; }

    public HttpStatusCode? StatusCode { get; }
}
