using System.Text.Json;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.Abstractions;
using Trading.Strategy.Shared;

namespace Trading.Automation.Execution;

public sealed class DecisionAuditWriter
{
    private readonly PromptObservabilityOptions _options;

    public DecisionAuditWriter(IOptions<PromptObservabilityOptions> options)
    {
        _options = options.Value;
    }

    public async Task<ArtifactReference> WriteInitialAsync(
        IntradayOpportunityPreparationDocument prepared,
        IntradayOpportunityExecutionArtifacts executionArtifacts,
        IntradayOpportunityReviewResult workflowResult,
        CancellationToken cancellationToken = default)
    {
        var record = CreateInitialRecord(prepared, executionArtifacts, workflowResult);
        var path = BuildAuditPath(prepared.TradingDate, prepared.RequestedAtUtc);
        await SaveAsync(path, record, cancellationToken);
        return ToArtifactReference(path);
    }

    public async Task<DecisionAuditRecord> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<DecisionAuditRecord>(json, DecisionAuditJson.Options)
            ?? throw new InvalidOperationException($"Decision audit record '{path}' could not be deserialized.");
    }

    public async Task SaveAsync(
        string path,
        DecisionAuditRecord record,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record, DecisionAuditJson.Options), cancellationToken);
    }

    public IReadOnlyList<string> FindAuditFiles(string rootPath, DateOnly? tradingDate = null)
    {
        var root = Path.GetFullPath(rootPath);
        var searchRoot = tradingDate is null
            ? root
            : Path.Combine(root, tradingDate.Value.ToString("yyyy-MM-dd"));

        if (!Directory.Exists(searchRoot))
        {
            return [];
        }

        return Directory.GetFiles(searchRoot, "*-decision-audit.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private DecisionAuditRecord CreateInitialRecord(
        IntradayOpportunityPreparationDocument prepared,
        IntradayOpportunityExecutionArtifacts executionArtifacts,
        IntradayOpportunityReviewResult workflowResult)
    {
        var assessments = workflowResult.MarketAssessments
            .Select(ToAuditAssessment)
            .ToArray();
        var candidates = workflowResult.CandidateOpportunities
            .Select(ToAuditCandidate)
            .ToArray();
        var promptMetadata = ReadPromptMetadata(executionArtifacts.PromptEnvelopeArtifact.Path);

        return new DecisionAuditRecord(
            prepared.TradingDate,
            workflowResult.ReviewedAtUtc,
            DateTimeOffset.UtcNow,
            candidates.Length == 0 ? DecisionAuditDecision.NoCandidate : DecisionAuditDecision.PaperOnly,
            candidates.Length == 0
                ? "No actionable candidates were returned; stand-aside decision captured for later review."
                : "Candidates captured for paper evaluation only. No broker order was placed.",
            new PromptAuditReference(
                prepared.PreparedArtifact,
                prepared.RequestTextArtifact,
                executionArtifacts.PromptEnvelopeArtifact,
                executionArtifacts.ExtractedJsonArtifact,
                promptMetadata.ModelId,
                promptMetadata.ProcessingMode,
                promptMetadata.ProviderResponseId,
                promptMetadata.ProviderStatus),
            assessments,
            candidates,
            candidates
                .Select(candidate => new PaperTradeOutcome(
                    candidate.Instrument,
                    candidate.Direction,
                    PaperTradeOutcomeStatus.DataInsufficient,
                    DateTimeOffset.UtcNow,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    candidate.CurrentSpread,
                    CalculateSpreadCostR(candidate),
                    0,
                    "Paper outcome has not been evaluated against post-signal market data yet."))
                .ToArray(),
            assessments
                .Select(assessment => new PaperMarketAssessmentOutcome(
                    assessment.Instrument,
                    assessment.DirectionalBias,
                    PaperMarketAssessmentOutcomeStatus.DataInsufficient,
                    DateTimeOffset.UtcNow,
                    workflowResult.ReviewedAtUtc.AddHours(1),
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    0,
                    "Assessment follow-through has not been evaluated against post-signal market data yet."))
                .ToArray(),
            DecisionBiasSummary.From(assessments, candidates));
    }

    private string BuildAuditPath(DateOnly tradingDate, DateTimeOffset requestedAtUtc)
    {
        var rootPath = Path.GetFullPath(_options.ObservabilityRootPath);
        var dayPath = Path.Combine(rootPath, tradingDate.ToString("yyyy-MM-dd"));
        return Path.Combine(dayPath, $"{requestedAtUtc:HHmmssfff}-decision-audit.json");
    }

    private static DecisionAuditAssessment ToAuditAssessment(IntradayMarketAssessment assessment)
        => new(
            assessment.Instrument,
            assessment.InstrumentName,
            assessment.OpportunityScore,
            assessment.DirectionalBias,
            assessment.Summary,
            assessment.WhyNow,
            assessment.StandAsideReason);

    private static DecisionAuditCandidate ToAuditCandidate(IntradayOpportunityCandidate candidate)
        => new(
            candidate.Instrument,
            candidate.InstrumentName,
            candidate.Direction,
            candidate.OpportunityScore,
            candidate.EntryMethod,
            candidate.EntryPrice,
            candidate.StopLossPrice,
            candidate.TakeProfitPrice,
            candidate.RewardRiskRatio,
            candidate.CurrentPrice,
            candidate.CurrentSpread,
            candidate.Thesis,
            candidate.Invalidation,
            candidate.WhyNow,
            candidate.SetupExpiresAtUtc);

    private static decimal? CalculateSpreadCostR(DecisionAuditCandidate candidate)
    {
        var risk = Math.Abs(candidate.EntryPrice - candidate.StopLossPrice);
        return risk > 0m ? candidate.CurrentSpread / risk : null;
    }

    private static ArtifactReference ToArtifactReference(string path)
        => new(Path.GetFullPath(path), new Uri(Path.GetFullPath(path)).AbsoluteUri);

    private static PromptMetadata ReadPromptMetadata(string promptEnvelopePath)
    {
        if (!File.Exists(promptEnvelopePath))
        {
            return new PromptMetadata(null, null, null, null);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(promptEnvelopePath));
        var root = document.RootElement;
        return new PromptMetadata(
            TryGetString(root, "modelId"),
            TryGetString(root, "processingMode"),
            TryGetString(root, "providerResponseId"),
            TryGetString(root, "providerStatus"));
    }

    private static string? TryGetString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private sealed record PromptMetadata(
        string? ModelId,
        string? ProcessingMode,
        string? ProviderResponseId,
        string? ProviderStatus);
}
