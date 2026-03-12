using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.PromptExecution;

namespace Trading.AI.Observability;

public sealed class PromptObservabilityWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly PromptObservabilityOptions _options;

    public PromptObservabilityWriter(IOptions<PromptObservabilityOptions> options)
    {
        _options = options.Value;
    }

    internal async Task<PromptObservationSession> StartAsync(PromptInvocation invocation, string requestText, object? requestOptions, CancellationToken cancellationToken)
    {
        var basePath = BuildBasePath(invocation.PromptDate, invocation.Prompt.Name, invocation.RequestedAtUtc);
        var session = new PromptObservationSession(
            $"{basePath}.json",
            basePath,
            BuildTextArtifactPath(basePath, invocation.TextArtifactKind),
            $"{basePath}-extracted.json");

        var record = new PromptObservationRecord
        {
            PromptId = invocation.Prompt.Id,
            PromptName = invocation.Prompt.Name,
            Status = "Pending",
            RequestedAtUtc = invocation.RequestedAtUtc,
            PromptDate = invocation.PromptDate,
            ModelId = invocation.Model.ModelId,
            RequestText = requestText,
            RequestOptions = requestOptions,
        };

        await WriteJsonAsync(session.JsonPath, record, cancellationToken);
        return session;
    }

    internal async Task WriteAttachmentsAsync(
        PromptObservationSession session,
        IReadOnlyList<PromptAttachment> attachments,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < attachments.Count; index++)
        {
            var attachment = attachments[index];
            var extension = GetFileExtension(attachment.MediaType);
            var labelSlug = ToSlug(attachment.Label);
            var path = $"{session.BasePath}-{index + 1:D2}-{labelSlug}{extension}";
            await File.WriteAllBytesAsync(path, attachment.Data, cancellationToken);
            session.AttachmentArtifactPaths.Add(path);
        }
    }

    internal Task WriteTextAsync(PromptObservationSession session, string text, CancellationToken cancellationToken)
        => string.IsNullOrWhiteSpace(session.TextArtifactPath)
            ? Task.CompletedTask
            : File.WriteAllTextAsync(session.TextArtifactPath, text, cancellationToken);

    internal Task WriteStructuredAsync(PromptObservationSession session, object structuredResponse, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(session.StructuredArtifactPath, JsonSerializer.Serialize(structuredResponse, JsonOptions), cancellationToken);

    internal async Task CompleteAsync(
        PromptObservationSession session,
        PromptInvocation invocation,
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
        var cost = CalculateCost(usage, cachedInputTokens, invocation.Model.Pricing);

        var record = new PromptObservationRecord
        {
            PromptId = invocation.Prompt.Id,
            PromptName = invocation.Prompt.Name,
            Status = "Completed",
            RequestedAtUtc = invocation.RequestedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            PromptDate = invocation.PromptDate,
            ModelId = response.ModelId ?? invocation.Model.ModelId,
            RequestText = requestText,
            RequestOptions = requestOptions,
            ResponseText = responseText,
            StructuredResponse = structuredResponse,
            Usage = usage,
            Cost = cost,
            RawResponse = ConvertRawRepresentation(response.RawRepresentation),
            DurationMs = duration.TotalMilliseconds,
            TextArtifactPath = TryGetArtifactPath(session.TextArtifactPath),
            StructuredArtifactPath = TryGetArtifactPath(session.StructuredArtifactPath),
            AttachmentArtifactPaths = TryGetArtifactPaths(session.AttachmentArtifactPaths),
        };

        await WriteJsonAsync(session.JsonPath, record, cancellationToken);
    }

    internal async Task FailAsync(
        PromptObservationSession session,
        PromptInvocation invocation,
        string requestText,
        object? requestOptions,
        Exception exception,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var record = new PromptObservationRecord
        {
            PromptId = invocation.Prompt.Id,
            PromptName = invocation.Prompt.Name,
            Status = "Failed",
            RequestedAtUtc = invocation.RequestedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            PromptDate = invocation.PromptDate,
            ModelId = invocation.Model.ModelId,
            RequestText = requestText,
            RequestOptions = requestOptions,
            Error = exception.ToString(),
            DurationMs = duration.TotalMilliseconds,
            TextArtifactPath = TryGetArtifactPath(session.TextArtifactPath),
            StructuredArtifactPath = TryGetArtifactPath(session.StructuredArtifactPath),
            AttachmentArtifactPaths = TryGetArtifactPaths(session.AttachmentArtifactPaths),
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

    private static string? BuildTextArtifactPath(string basePath, PromptTextArtifactKind artifactKind)
        => artifactKind switch
        {
            PromptTextArtifactKind.Text => $"{basePath}.txt",
            PromptTextArtifactKind.Markdown => $"{basePath}.md",
            _ => null,
        };

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

    private static string? TryGetArtifactPath(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;

    private static IReadOnlyList<string>? TryGetArtifactPaths(IEnumerable<string> paths)
    {
        var existing = paths.Where(File.Exists).ToArray();
        return existing.Length == 0 ? null : existing;
    }

    private static string GetFileExtension(string mediaType)
        => mediaType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "text/plain" => ".txt",
            _ => ".bin",
        };

    private static string ToSlug(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var count = 0;

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[count++] = char.ToLowerInvariant(character);
                continue;
            }

            if (count > 0 && buffer[count - 1] != '-')
            {
                buffer[count++] = '-';
            }
        }

        return count == 0 ? "attachment" : new string(buffer[..count]).Trim('-');
    }
}
