using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Trading.AI.Prompts.IntradayOpportunityReview;

public static class IntradayOpportunityReviewResponseFormat
{
    private const string ResourceName = "Trading.AI.Prompts.IntradayOpportunityReview.IntradayOpportunityReview.schema.json";

    public static ChatResponseFormat Create()
    {
        var schema = LoadSchemaText();
        using var document = JsonDocument.Parse(schema);
        return ChatResponseFormat.ForJsonSchema(
            document.RootElement.Clone(),
            "intraday_opportunity_review",
            "Structured intraday opportunity review.");
    }

    public static string GetSchemaSha256()
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(LoadSchemaText()))).ToLowerInvariant();

    private static string LoadSchemaText()
    {
        using var stream = typeof(IntradayOpportunityReviewResponseFormat).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Prompt schema resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
