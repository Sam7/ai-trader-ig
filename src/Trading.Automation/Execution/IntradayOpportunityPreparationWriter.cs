using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.Prompts;

namespace Trading.Automation.Execution;

public sealed class IntradayOpportunityPreparationWriter
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
        await File.WriteAllTextAsync(requestTextPath, preparedRun.RequestText, cancellationToken);

        var attachmentArtifacts = new List<IntradayOpportunityPreparedAttachment>(preparedRun.Markets.Count);
        var preparedMarkets = new List<IntradayOpportunityPreparedMarket>(preparedRun.Markets.Count);

        for (var index = 0; index < preparedRun.Markets.Count; index++)
        {
            var market = preparedRun.Markets[index];
            var chartPath = $"{basePath}-{index + 1:D2}-{ToSlug(market.InstrumentName)}.png";
            await File.WriteAllBytesAsync(chartPath, market.ChartBytes, cancellationToken);
            var artifact = ToArtifactReference(chartPath);

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
                artifact));

            attachmentArtifacts.Add(new IntradayOpportunityPreparedAttachment(
                market.AttachmentLabel,
                "image/png",
                artifact));
        }

        var documentPath = $"{basePath}.json";
        var documentArtifact = ToArtifactReference(documentPath);
        var requestTextArtifact = ToArtifactReference(requestTextPath);
        var document = new IntradayOpportunityPreparationDocument(
            tradingDate,
            requestedAtUtc,
            PromptRegistry.IntradayOpportunityReview.Id,
            preparedRun.Input,
            preparedRun.RequestText,
            preparedMarkets,
            attachmentArtifacts,
            documentArtifact,
            requestTextArtifact);

        await File.WriteAllTextAsync(documentPath, JsonSerializer.Serialize(document, JsonOptions), cancellationToken);
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
}
