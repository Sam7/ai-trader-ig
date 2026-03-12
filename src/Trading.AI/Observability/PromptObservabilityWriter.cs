using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.DailyBriefing;

namespace Trading.AI.Observability;

public sealed class PromptObservabilityWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly DailyBriefingOptions _options;

    public PromptObservabilityWriter(IOptions<DailyBriefingOptions> options)
    {
        _options = options.Value;
    }

    public async Task<PromptObservationSession> StartAsync(PromptExecutionContext context, string requestText, object? requestOptions, CancellationToken cancellationToken)
    {
        var basePath = BuildBasePath(context.TradingDate, context.Prompt.Name, context.RequestedAtUtc);
        var session = new PromptObservationSession($"{basePath}.json", basePath);

        var record = new PromptObservationRecord
        {
            PromptId = context.Prompt.Id,
            PromptName = context.Prompt.Name,
            Status = "Pending",
            RequestedAtUtc = context.RequestedAtUtc,
            TradingDate = context.TradingDate,
            ModelId = context.Model.ModelId,
            RequestText = requestText,
            RequestOptions = requestOptions,
        };

        await WriteJsonAsync(session.JsonPath, record, cancellationToken);
        return session;
    }

    public Task WriteMarkdownAsync(PromptObservationSession session, string markdown, CancellationToken cancellationToken)
        => File.WriteAllTextAsync($"{session.BasePath}.md", markdown, cancellationToken);

    public Task WriteStructuredAsync(PromptObservationSession session, object structuredResponse, CancellationToken cancellationToken)
        => File.WriteAllTextAsync($"{session.BasePath}-extracted.json", JsonSerializer.Serialize(structuredResponse, JsonOptions), cancellationToken);

    public async Task CompleteAsync(
        PromptObservationSession session,
        PromptExecutionContext context,
        string requestText,
        object? requestOptions,
        ChatResponse response,
        string responseText,
        object? structuredResponse,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var usage = response.Usage ?? new UsageDetails();
        var cachedInputTokens = TryGetCachedInputTokenCount(usage);
        var cost = CalculateCost(usage, cachedInputTokens, context.Model.Pricing);

        var record = new PromptObservationRecord
        {
            PromptId = context.Prompt.Id,
            PromptName = context.Prompt.Name,
            Status = "Completed",
            RequestedAtUtc = context.RequestedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            TradingDate = context.TradingDate,
            ModelId = response.ModelId ?? context.Model.ModelId,
            RequestText = requestText,
            RequestOptions = requestOptions,
            ResponseText = responseText,
            StructuredResponse = structuredResponse,
            Usage = usage,
            Cost = cost,
            RawResponse = ConvertRawRepresentation(response.RawRepresentation),
            DurationMs = duration.TotalMilliseconds,
            MarkdownArtifactPath = TryGetMarkdownPath(session),
            StructuredArtifactPath = TryGetStructuredPath(session),
        };

        await WriteJsonAsync(session.JsonPath, record, cancellationToken);
    }

    public async Task FailAsync(
        PromptObservationSession session,
        PromptExecutionContext context,
        string requestText,
        object? requestOptions,
        Exception exception,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var record = new PromptObservationRecord
        {
            PromptId = context.Prompt.Id,
            PromptName = context.Prompt.Name,
            Status = "Failed",
            RequestedAtUtc = context.RequestedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            TradingDate = context.TradingDate,
            ModelId = context.Model.ModelId,
            RequestText = requestText,
            RequestOptions = requestOptions,
            Error = exception.ToString(),
            DurationMs = duration.TotalMilliseconds,
            MarkdownArtifactPath = TryGetMarkdownPath(session),
            StructuredArtifactPath = TryGetStructuredPath(session),
        };

        await WriteJsonAsync(session.JsonPath, record, cancellationToken);
    }

    private string BuildBasePath(DateOnly tradingDate, string promptName, DateTimeOffset requestedAtUtc)
    {
        var rootPath = Path.GetFullPath(_options.ObservabilityRootPath);
        var dayPath = Path.Combine(rootPath, tradingDate.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dayPath);
        var filePrefix = $"{requestedAtUtc:HHmmssfff}-{promptName}";
        return Path.Combine(dayPath, filePrefix);
    }

    private static async Task WriteJsonAsync(string path, PromptObservationRecord record, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record, JsonOptions), cancellationToken);
    }

    private static int TryGetCachedInputTokenCount(UsageDetails usage)
    {
        if (usage.AdditionalCounts is null)
        {
            return 0;
        }

        foreach (var (key, value) in usage.AdditionalCounts)
        {
            if (key.Contains("cached", StringComparison.OrdinalIgnoreCase))
            {
                return checked((int)value);
            }
        }

        return 0;
    }

    private static CostBreakdown CalculateCost(UsageDetails usage, int cachedInputTokens, ModelPricingOptions pricing)
    {
        var inputTokens = checked((int)usage.InputTokenCount.GetValueOrDefault());
        var outputTokens = checked((int)usage.OutputTokenCount.GetValueOrDefault());

        var inputCost = CalculateMillionTokenPrice(inputTokens - cachedInputTokens, pricing.InputUsdPerMillionTokens);
        var cachedInputCost = CalculateMillionTokenPrice(cachedInputTokens, pricing.CachedInputUsdPerMillionTokens);
        var outputCost = CalculateMillionTokenPrice(outputTokens, pricing.OutputUsdPerMillionTokens);

        return new CostBreakdown(
            inputTokens,
            outputTokens,
            cachedInputTokens,
            inputCost,
            outputCost,
            cachedInputCost,
            inputCost + outputCost + cachedInputCost);
    }

    private static decimal CalculateMillionTokenPrice(int tokens, decimal usdPerMillionTokens)
        => tokens <= 0 ? 0m : decimal.Round((tokens / 1_000_000m) * usdPerMillionTokens, 8, MidpointRounding.AwayFromZero);

    private static object? ConvertRawRepresentation(object? rawRepresentation)
    {
        if (rawRepresentation is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.SerializeToElement(rawRepresentation, rawRepresentation.GetType(), JsonOptions);
        }
        catch
        {
            return rawRepresentation.ToString();
        }
    }

    private static string? TryGetMarkdownPath(PromptObservationSession session)
    {
        var path = $"{session.BasePath}.md";
        return File.Exists(path) ? path : null;
    }

    private static string? TryGetStructuredPath(PromptObservationSession session)
    {
        var path = $"{session.BasePath}-extracted.json";
        return File.Exists(path) ? path : null;
    }
}
