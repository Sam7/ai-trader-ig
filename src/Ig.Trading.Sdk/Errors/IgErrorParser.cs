using System.Net;
using System.Text.Json;
using Refit;

namespace Ig.Trading.Sdk.Errors;

internal static class IgErrorParser
{
    public static IgApiException ToIgApiException(ApiException exception)
    {
        return Create(exception.StatusCode, exception.Content, exception);
    }

    public static IgApiException Create(HttpStatusCode? statusCode, string? content, Exception? innerException = null)
    {
        var errorCode = TryGetErrorCode(content);
        var message = string.IsNullOrWhiteSpace(errorCode)
            ? $"IG API request failed with status code {(int?)statusCode ?? 0}."
            : $"IG API error: {errorCode}.";

        return new IgApiException(errorCode, statusCode, message, innerException);
    }

    private static string? TryGetErrorCode(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("errorCode", out var errorCodeElement))
            {
                return errorCodeElement.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
