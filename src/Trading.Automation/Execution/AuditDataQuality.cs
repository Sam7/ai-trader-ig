using Trading.MarketData;

namespace Trading.Automation.Execution;

public sealed record AuditDataQualityPolicy(
    bool StrictData = false,
    int MaxAssessmentInteriorMissingBars = 1,
    int MaxAssessmentConsecutiveMissingBars = 1,
    decimal MaxAssessmentMissingRatio = 0.10m);

public enum AuditDataQualityUseCase
{
    Candidate = 1,
    Assessment = 2,
}

public enum AuditDataQualityClassification
{
    Complete = 0,
    EvaluatedWithToleratedGaps = 1,
    ClosedMarket = 2,
    AbnormalNonTradeable = 3,
    UnsafeUnknownGaps = 4,
    InsufficientTailData = 5,
    NoBars = 6,
}

public sealed record AuditDataQualityResult(
    AuditDataQualityUseCase UseCase,
    AuditDataQualityClassification Classification,
    MarketDataGap? FirstIssue,
    int ExpectedBars,
    int FinalBars,
    int UnknownMissingBars,
    int MaxConsecutiveUnknownMissingBars,
    int ClosedMarketBars,
    int AbnormalNonTradeableBars,
    int NonFinalOnlyBars,
    int KnownNoBarsWithoutSessionBars,
    string Reason)
{
    public bool HasUnsafeUnknownGap
        => Classification is AuditDataQualityClassification.UnsafeUnknownGaps
            or AuditDataQualityClassification.InsufficientTailData
            or AuditDataQualityClassification.NoBars
            or AuditDataQualityClassification.AbnormalNonTradeable;
}

public sealed record DecisionAuditDataQualitySummary(
    int CompleteWindows,
    int EvaluatedWithToleratedGaps,
    int ClosedMarketWindows,
    int AbnormalNonTradeableWindows,
    int UnsafeUnknownGapWindows,
    int InsufficientTailWindows,
    int NoBarsWindows)
{
    public static DecisionAuditDataQualitySummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    public static DecisionAuditDataQualitySummary From(IReadOnlyList<AuditDataQualityResult> results)
        => new(
            results.Count(result => result.Classification == AuditDataQualityClassification.Complete),
            results.Count(result => result.Classification == AuditDataQualityClassification.EvaluatedWithToleratedGaps),
            results.Count(result => result.Classification == AuditDataQualityClassification.ClosedMarket),
            results.Count(result => result.Classification == AuditDataQualityClassification.AbnormalNonTradeable),
            results.Count(result => result.Classification == AuditDataQualityClassification.UnsafeUnknownGaps),
            results.Count(result => result.Classification == AuditDataQualityClassification.InsufficientTailData),
            results.Count(result => result.Classification == AuditDataQualityClassification.NoBars));
}
