using System.Text.Json;
using Refit;

namespace Ig.Trading.Sdk.Errors;

internal static class IgErrorParser
{
    public static IgApiException ToIgApiException(ApiException exception)
    {
        var content = exception.Content;
        var errorCode = TryGetErrorCode(content);
        var message = string.IsNullOrWhiteSpace(errorCode)
            ? $"IG API request failed with status code {(int)exception.StatusCode}."
            : $"IG API error: {errorCode}.";

        return new IgApiException(errorCode, exception.StatusCode, message, exception);
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
            return content;
        }

        return content;
    }
}
