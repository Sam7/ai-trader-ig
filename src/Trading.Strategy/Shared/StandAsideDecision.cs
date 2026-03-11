namespace Trading.Strategy.Shared;

public enum StandAsideReason
{
    NoSetup = 0,
    WeakEdge = 1,
    ContradictoryNews = 2,
    VolatilityTooErratic = 3,
    RewardRiskTooLow = 4,
    DuplicateExposure = 5,
    DailyLimitReached = 6,
    ExposureLimitReached = 7,
    SpreadTooWide = 8,
    SlippageTooHigh = 9,
    EventWindowBlocked = 10,
    ApprovalRejected = 11,
}

public sealed record StandAsideDecision(
    StandAsideReason Reason,
    string Summary,
    DateTimeOffset DecidedAtUtc);
