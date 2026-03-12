using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Trading.AI.Prompts.DailyPlanJson;

public static class DailyPlanJsonResponseFormat
{
    private const string ResourceName = "Trading.AI.Prompts.DailyPlanJson.DailyPlanJson.schema.json";
    private static readonly Assembly Assembly = typeof(DailyPlanJsonResponseFormat).Assembly;

    public static ChatResponseFormat Create(int watchListSize)
    {
        if (watchListSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(watchListSize), "Watch list size must be greater than zero.");
        }

        var size = watchListSize.ToString(CultureInfo.InvariantCulture);
        using var stream = Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Schema resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        var schemaText = reader.ReadToEnd().Replace("{{WATCHLIST_SIZE}}", size, StringComparison.Ordinal);
        var schema = JsonDocument.Parse(schemaText).RootElement.Clone();

        return ChatResponseFormat.ForJsonSchema(schema, "daily_plan_document", "Structured daily trading plan.");
    }
}
