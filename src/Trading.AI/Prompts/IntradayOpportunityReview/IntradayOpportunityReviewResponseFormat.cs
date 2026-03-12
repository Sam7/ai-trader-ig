using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Trading.AI.Prompts.IntradayOpportunityReview;

public static class IntradayOpportunityReviewResponseFormat
{
    private const string ResourceName = "Trading.AI.Prompts.IntradayOpportunityReview.IntradayOpportunityReview.schema.json";

    public static ChatResponseFormat Create()
    {
        var schema = LoadSchemaText();
        var document = JsonDocument.Parse(schema).RootElement.Clone();
        return ChatResponseFormat.ForJsonSchema(document, "intraday_opportunity_review", "Structured intraday opportunity review.");
    }

    private static string LoadSchemaText()
    {
        using var stream = typeof(IntradayOpportunityReviewResponseFormat).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Prompt schema resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        using var document = JsonDocument.Parse(reader.ReadToEnd());
        return document.RootElement.GetRawText();
    }
}
