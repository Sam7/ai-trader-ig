using System.Text.Json;
using System.Text.Json.Serialization;
using Trading.Abstractions;
using Trading.Strategy.Shared;

namespace Trading.Automation.Execution;

public enum DecisionAuditDecision
{
    NoCandidate = 1,
    PaperOnly = 2,
    InvalidCandidate = 3,
    DataInsufficient = 4,
}

public enum PaperTradeOutcomeStatus
{
    DataInsufficient = 1,
    TargetHit = 2,
    StoppedOut = 3,
    Expired = 4,
    NoFill = 5,
}

public enum PaperMarketAssessmentOutcomeStatus
{
    DataInsufficient = 1,
    FollowedBias = 2,
    MovedAgainstBias = 3,
    Flat = 4,
}

public sealed record DecisionAuditRecord(
    DateOnly TradingDate,
    DateTimeOffset ReviewedAtUtc,
    DateTimeOffset GeneratedAtUtc,
    DecisionAuditDecision Decision,
    string Outcome,
    PromptAuditReference Prompt,
    IReadOnlyList<DecisionAuditAssessment> MarketAssessments,
    IReadOnlyList<DecisionAuditCandidate> CandidateOpportunities,
    IReadOnlyList<PaperTradeOutcome> PaperOutcomes,
    IReadOnlyList<PaperMarketAssessmentOutcome> MarketAssessmentOutcomes,
    DecisionBiasSummary BiasSummary);

public sealed record PromptAuditReference(
    ArtifactReference PreparedArtifact,
    ArtifactReference RequestTextArtifact,
    ArtifactReference PromptEnvelopeArtifact,
    ArtifactReference ExtractedJsonArtifact,
    string? ModelId,
    string? ProcessingMode,
    string? ProviderResponseId,
    string? ProviderStatus);

public sealed record DecisionAuditAssessment(
    InstrumentId Instrument,
    string InstrumentName,
    int OpportunityScore,
    TradeDirection DirectionalBias,
    string Summary,
    string WhyNow,
    string StandAsideReason);

public sealed record DecisionAuditCandidate(
    InstrumentId Instrument,
    string InstrumentName,
    TradeDirection Direction,
    int OpportunityScore,
    TradeEntryMethod EntryMethod,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TakeProfitPrice,
    decimal RewardRiskRatio,
    decimal CurrentPrice,
    decimal CurrentSpread,
    string Thesis,
    string Invalidation,
    string WhyNow,
    DateTimeOffset SetupExpiresAtUtc);

public sealed record PaperTradeOutcome(
    InstrumentId Instrument,
    TradeDirection Direction,
    PaperTradeOutcomeStatus Status,
    DateTimeOffset EvaluatedAtUtc,
    DateTimeOffset? FilledAtUtc,
    DateTimeOffset? ClosedAtUtc,
    decimal? EntryPrice,
    decimal? ExitPrice,
    decimal? EstimatedRMultiple,
    decimal? MaxFavorableExcursion,
    decimal? MaxAdverseExcursion,
    decimal SpreadCost,
    decimal? SpreadCostR,
    int BarsEvaluated,
    string Reason);

public sealed record PaperMarketAssessmentOutcome(
    InstrumentId Instrument,
    TradeDirection DirectionalBias,
    PaperMarketAssessmentOutcomeStatus Status,
    DateTimeOffset EvaluatedAtUtc,
    DateTimeOffset HorizonEndsAtUtc,
    decimal? StartPrice,
    decimal? EndPrice,
    decimal? DirectionalMove,
    decimal? DirectionalMovePercent,
    decimal? MaxFavorableExcursion,
    decimal? MaxAdverseExcursion,
    int BarsEvaluated,
    string Reason);

public sealed record DecisionBiasSummary(
    int AssessmentCount,
    int CandidateCount,
    int BuyAssessmentCount,
    int SellAssessmentCount,
    int BuyCandidateCount,
    int SellCandidateCount,
    string DominantAssessmentDirection,
    string DominantCandidateDirection,
    IReadOnlyDictionary<string, int> CandidateCountByInstrument)
{
    public static DecisionBiasSummary From(
        IReadOnlyList<DecisionAuditAssessment> assessments,
        IReadOnlyList<DecisionAuditCandidate> candidates)
    {
        var buyAssessments = assessments.Count(assessment => assessment.DirectionalBias == TradeDirection.Buy);
        var sellAssessments = assessments.Count(assessment => assessment.DirectionalBias == TradeDirection.Sell);
        var buyCandidates = candidates.Count(candidate => candidate.Direction == TradeDirection.Buy);
        var sellCandidates = candidates.Count(candidate => candidate.Direction == TradeDirection.Sell);

        return new DecisionBiasSummary(
            assessments.Count,
            candidates.Count,
            buyAssessments,
            sellAssessments,
            buyCandidates,
            sellCandidates,
            ResolveDominantDirection(buyAssessments, sellAssessments),
            ResolveDominantDirection(buyCandidates, sellCandidates),
            candidates
                .GroupBy(candidate => candidate.Instrument.Value, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));
    }

    private static string ResolveDominantDirection(int buyCount, int sellCount)
    {
        if (buyCount == 0 && sellCount == 0)
        {
            return "None";
        }

        if (buyCount == sellCount)
        {
            return "Balanced";
        }

        return buyCount > sellCount ? "Buy" : "Sell";
    }
}

public sealed record DecisionAuditEvaluationRequest(
    string RootPath,
    DateOnly? TradingDate,
    PriceResolution Resolution,
    bool StrictData = false,
    int MaxAssessmentInteriorMissingBars = 1,
    int MaxAssessmentConsecutiveMissingBars = 1,
    decimal MaxAssessmentMissingRatio = 0.10m)
{
    public AuditDataQualityPolicy CreateDataQualityPolicy()
        => new(
            StrictData,
            MaxAssessmentInteriorMissingBars,
            MaxAssessmentConsecutiveMissingBars,
            MaxAssessmentMissingRatio);
}

public sealed record DecisionAuditEvaluationReport(
    string RootPath,
    DateOnly? TradingDate,
    PriceResolution Resolution,
    DateTimeOffset EvaluatedAtUtc,
    int RecordsEvaluated,
    int CandidatesEvaluated,
    int TargetHitCount,
    int StoppedOutCount,
    int ExpiredCount,
    int NoFillCount,
    int DataInsufficientCount,
    int AssessmentsEvaluated,
    int AssessmentFollowedBiasCount,
    int AssessmentMovedAgainstBiasCount,
    int AssessmentFlatCount,
    int AssessmentDataInsufficientCount,
    decimal? AverageEstimatedRMultiple,
    DecisionBiasSummary BiasSummary,
    ArtifactReference? ReportArtifact,
    DecisionAuditDataQualitySummary? DataQuality = null);

internal static class DecisionAuditJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
            new InstrumentIdJsonConverter(),
        },
    };
}

internal sealed class InstrumentIdJsonConverter : JsonConverter<InstrumentId>
{
    public override InstrumentId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => new(reader.GetString() ?? string.Empty);

    public override void Write(
        Utf8JsonWriter writer,
        InstrumentId value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
