using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.Prompts;

namespace Trading.Automation.Execution;

public sealed class IntradayOpportunityPreparationWriter : IIntradayOpportunityPreparationStore
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

    public IntradayOpportunityPreparationWriter(IOptions<PromptObservabilityOptions> options)
    {
        _options = options.Value;
    }

    public async Task<IntradayOpportunityPreparationDocument> WriteAsync(
        DateOnly tradingDate,
        DateTimeOffset requestedAtUtc,
        IntradayPreparedRun preparedRun,
        CancellationToken cancellationToken = default)
    {
        var basePath = BuildBasePath(tradingDate, requestedAtUtc);
        var requestTextPath = $"{basePath}-request.txt";
        await WriteNewAsync(requestTextPath, Encoding.UTF8.GetBytes(preparedRun.RequestText), cancellationToken);

        var attachments = new List<IntradayOpportunityPreparedAttachment>();
        var evidenceManifest = new List<DecisionEvidence>();
        var preparedMarkets = new List<IntradayOpportunityPreparedMarket>(preparedRun.Markets.Count);

        for (var index = 0; index < preparedRun.Markets.Count; index++)
        {
            var market = preparedRun.Markets[index];
            var evidenceIds = new List<string>(market.Evidence.Count);
            for (var evidenceIndex = 0; evidenceIndex < market.Evidence.Count; evidenceIndex++)
            {
                var preparedEvidence = market.Evidence[evidenceIndex];
                var sha256 = ComputeSha256(preparedEvidence.Data);
                var evidenceId = CreateEvidenceId(
                    tradingDate,
                    requestedAtUtc,
                    market.Instrument.Value,
                    preparedEvidence,
                    sha256);
                var evidencePath = $"{basePath}-{index + 1:D2}-{evidenceIndex + 1:D2}-{ToSlug(market.InstrumentName)}-{ToSlug(preparedEvidence.RecipeId)}{ResolveExtension(preparedEvidence.MediaType)}";
                await WriteNewAsync(evidencePath, preparedEvidence.Data, cancellationToken);

                evidenceIds.Add(evidenceId);
                evidenceManifest.Add(new DecisionEvidence(
                    evidenceId,
                    preparedEvidence.Kind,
                    preparedEvidence.Label,
                    market.Instrument,
                    preparedEvidence.MediaType,
                    ToArtifactReference(evidencePath),
                    preparedEvidence.WindowStartUtc,
                    preparedEvidence.WindowEndUtc,
                    preparedEvidence.AsOfUtc,
                    preparedEvidence.RecipeId,
                    preparedEvidence.RecipeVersion,
                    sha256));

                if (preparedEvidence.AttachToPrompt)
                {
                    attachments.Add(new IntradayOpportunityPreparedAttachment(
                        evidenceId,
                        preparedEvidence.Label,
                        preparedEvidence.MediaType));
                }
            }

            preparedMarkets.Add(new IntradayOpportunityPreparedMarket(
                market.Instrument.Value,
                market.InstrumentName,
                market.Rank,
                market.CurrentBid,
                market.CurrentAsk,
                market.CurrentPrice,
                market.CurrentSpread,
                market.LatestBarAtUtc,
                market.PriceSeriesRefreshMode,
                market.FetchedBarCount,
                evidenceIds));
        }

        var documentPath = $"{basePath}.json";
        var documentArtifact = ToArtifactReference(documentPath);
        var requestTextArtifact = ToArtifactReference(requestTextPath);
        var document = new IntradayOpportunityPreparationDocument(
            tradingDate,
            requestedAtUtc,
            PromptRegistry.IntradayOpportunityReview.Id,
            preparedRun.Request,
            preparedRun.RequestText,
            preparedMarkets,
            attachments,
            documentArtifact,
            requestTextArtifact)
        {
            PreparationProfile = preparedRun.PreparationProfile,
            PromptContract = preparedRun.PromptContract,
            RequestSha256 = ComputeSha256(Encoding.UTF8.GetBytes(preparedRun.RequestText)),
            Evidence = evidenceManifest,
        };

        await WriteNewAsync(
            documentPath,
            JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions),
            cancellationToken);
        return document;
    }

    public async Task<IntradayOpportunityPreparationDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<IntradayOpportunityPreparationDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Preparation document '{path}' could not be deserialized.");
    }

    private string BuildBasePath(DateOnly tradingDate, DateTimeOffset requestedAtUtc)
    {
        var rootPath = Path.GetFullPath(_options.ObservabilityRootPath);
        var dayPath = Path.Combine(rootPath, tradingDate.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dayPath);
        return Path.Combine(dayPath, $"{requestedAtUtc:HHmmssfff}-intraday-opportunity-prepare");
    }

    private static ArtifactReference ToArtifactReference(string path)
        => new(Path.GetFullPath(path), new Uri(Path.GetFullPath(path)).AbsoluteUri);

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

        return count == 0 ? "chart" : new string(buffer[..count]).Trim('-');
    }

    private static string CreateEvidenceId(
        DateOnly tradingDate,
        DateTimeOffset requestedAtUtc,
        string instrumentId,
        PreparedDecisionEvidence evidence,
        string contentSha256)
    {
        var identity = string.Join(
            "|",
            tradingDate.ToString("yyyy-MM-dd"),
            requestedAtUtc.ToUniversalTime().ToString("O"),
            instrumentId,
            evidence.Kind,
            evidence.RecipeId,
            evidence.RecipeVersion,
            contentSha256);
        return $"ev_{ComputeSha256(Encoding.UTF8.GetBytes(identity))[..24]}";
    }

    internal static string ComputeSha256(byte[] data)
        => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static async Task WriteNewAsync(
        string path,
        byte[] data,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await stream.WriteAsync(data, cancellationToken);
    }

    private static string ResolveExtension(string mediaType)
        => mediaType switch
        {
            "image/png" => ".png",
            "application/json" => ".json",
            "text/markdown" => ".md",
            "text/plain" => ".txt",
            _ => ".bin",
        };
}
