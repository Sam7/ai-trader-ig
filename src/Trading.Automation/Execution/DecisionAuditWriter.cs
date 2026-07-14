using System.Text.Json;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.Abstractions;
using Trading.Execution;
using Trading.Strategy.Shared;

namespace Trading.Automation.Execution;

public sealed class DecisionAuditWriter
{
    private readonly PromptObservabilityOptions _options;

    public DecisionAuditWriter(IOptions<PromptObservabilityOptions> options)
    {
        _options = options.Value;
    }

    public Task<ArtifactReference> WriteInitialAsync(
        IntradayOpportunityPreparationDocument prepared,
        IntradayOpportunityExecutionArtifacts executionArtifacts,
        IntradayOpportunityReviewResult workflowResult,
        CancellationToken cancellationToken = default)
        => WriteInitialAsync(prepared, executionArtifacts, workflowResult, null, cancellationToken);

    public async Task<ArtifactReference> WriteInitialAsync(
        IntradayOpportunityPreparationDocument prepared,
        IntradayOpportunityExecutionArtifacts executionArtifacts,
        IntradayOpportunityReviewResult workflowResult,
        ExecutionBoundarySnapshot? executionBoundary = null,
        CancellationToken cancellationToken = default)
    {
        var record = CreateInitialRecord(prepared, executionArtifacts, workflowResult, executionBoundary);
        var path = BuildAuditPath(prepared.TradingDate, prepared.RequestedAtUtc);
        await WriteNewAsync(path, record, cancellationToken);
        return ToArtifactReference(path);
    }

    public async Task<DecisionAuditRecord> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var record = JsonSerializer.Deserialize<DecisionAuditRecord>(json, DecisionAuditJson.Options)
            ?? throw new InvalidOperationException($"Decision audit record '{path}' could not be deserialized.");
        return NormalizeLoadedRecord(path, record);
    }

    internal async Task SaveAsync(
        string path,
        DecisionAuditRecord record,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record, DecisionAuditJson.Options), cancellationToken);
    }

    private static async Task WriteNewAsync(
        string path,
        DecisionAuditRecord record,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, record, DecisionAuditJson.Options, cancellationToken);
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
        IntradayOpportunityReviewResult workflowResult,
        ExecutionBoundarySnapshot? executionBoundary)
    {
        var assessments = workflowResult.MarketAssessments
            .Select(ToAuditAssessment)
            .ToArray();
        var candidates = workflowResult.CandidateOpportunities
            .Select(ToAuditCandidate)
            .ToArray();
        var promptMetadata = ReadPromptMetadata(executionArtifacts.PromptEnvelopeArtifact.Path);
        var auditId = CreateAuditId(prepared.TradingDate, prepared.RequestedAtUtc);

        return new DecisionAuditRecord(
            auditId,
            prepared.TradingDate,
            workflowResult.ReviewedAtUtc,
            DateTimeOffset.UtcNow,
            ResolveDecision(workflowResult),
            ResolveOutcome(workflowResult, candidates.Length),
            new PromptAuditReference(
                prepared.PreparedArtifact,
                prepared.RequestTextArtifact,
                executionArtifacts.PromptEnvelopeArtifact,
                executionArtifacts.ExtractedJsonArtifact,
                promptMetadata.ModelId,
                promptMetadata.ProcessingMode,
                promptMetadata.ProviderResponseId,
                promptMetadata.ProviderStatus)
            {
                PreparationSchemaVersion = prepared.SchemaVersion,
                PreparationProfile = prepared.PreparationProfile,
                PromptContract = prepared.PromptContract,
                RequestSha256 = prepared.RequestSha256,
            },
            assessments,
            candidates,
            workflowResult.ExecutionMode,
            workflowResult.CandidateDecisions,
            workflowResult.SelectedShadowIntent,
            executionBoundary,
            workflowResult.DecisionSummary,
            DecisionBiasSummary.From(assessments, candidates))
        {
            Evidence = prepared.Evidence,
        };
    }

    private string BuildAuditPath(DateOnly tradingDate, DateTimeOffset requestedAtUtc)
    {
        var rootPath = Path.GetFullPath(_options.ObservabilityRootPath);
        var dayPath = Path.Combine(rootPath, tradingDate.ToString("yyyy-MM-dd"));
        return Path.Combine(dayPath, $"{requestedAtUtc:HHmmssfff}-decision-audit.json");
    }

    public string CreateAuditId(DateOnly tradingDate, DateTimeOffset requestedAtUtc)
        => $"{tradingDate:yyyy-MM-dd}/{requestedAtUtc:HHmmssfff}-decision-audit";

    private static DecisionAuditDecision ResolveDecision(IntradayOpportunityReviewResult workflowResult)
    {
        if (workflowResult.CandidateOpportunities.Count == 0)
        {
            return DecisionAuditDecision.NoCandidate;
        }

        if (workflowResult.SelectedShadowIntent is not null)
        {
            return DecisionAuditDecision.ShadowApproved;
        }

        return workflowResult.CandidateDecisions.Count > 0
            ? DecisionAuditDecision.ShadowRejected
            : DecisionAuditDecision.PaperOnly;
    }

    private static string ResolveOutcome(IntradayOpportunityReviewResult workflowResult, int candidateCount)
    {
        if (candidateCount == 0)
        {
            return "No actionable candidates were returned; stand-aside decision captured for later review.";
        }

        if (workflowResult.SelectedShadowIntent is not null)
        {
            return "Candidates were evaluated for shadow execution and one execution-ready intent was selected. No broker order was placed.";
        }

        return "Candidates were evaluated for shadow execution, but no execution-ready intent was selected. No broker order was placed.";
    }

    private static DecisionAuditRecord NormalizeLoadedRecord(string path, DecisionAuditRecord record)
    {
        var shadowDecisions = record.ShadowDecisions ?? [];
        return record with
        {
            AuditId = string.IsNullOrWhiteSpace(record.AuditId) ? CreateAuditIdFromPath(path) : record.AuditId,
            ShadowDecisions = shadowDecisions,
            Evidence = record.Evidence ?? [],
            DecisionSummary = record.DecisionSummary ?? new IntradayCandidateDecisionSummary(
                record.CandidateOpportunities.Count,
                shadowDecisions.Count(decision => decision.Status == IntradayCandidateDecisionStatus.ApprovedForShadowExecution),
                shadowDecisions.Count(decision => decision.Status == IntradayCandidateDecisionStatus.Rejected),
                shadowDecisions.Count(decision => decision.Status == IntradayCandidateDecisionStatus.AlreadyProcessed),
                shadowDecisions.Count(decision => decision.Status == IntradayCandidateDecisionStatus.UnsupportedByCurrentExecutionScope)),
        };
    }

    private static string CreateAuditIdFromPath(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var day = Path.GetFileName(Path.GetDirectoryName(path));
        return string.IsNullOrWhiteSpace(day) ? fileName : $"{day}/{fileName}";
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
