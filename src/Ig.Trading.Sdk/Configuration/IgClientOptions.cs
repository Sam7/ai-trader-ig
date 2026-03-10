namespace Ig.Trading.Sdk.Configuration;

public sealed class IgClientOptions
{
    public const string SectionName = "IG";

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Identifier { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string? AccountId { get; set; }

    public bool UseDemo { get; set; } = true;

    public void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("IG BaseUrl must be a valid absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("IG ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(Identifier))
        {
            throw new InvalidOperationException("IG Identifier is required.");
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            throw new InvalidOperationException("IG Password is required.");
        }
    }
}
