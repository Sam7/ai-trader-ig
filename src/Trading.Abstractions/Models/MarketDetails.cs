namespace Trading.Abstractions;

public sealed record MarketDetails(
    InstrumentId Instrument,
    string Name,
    MarketStatus Status,
    string? Type,
    string? Expiry,
    string? CurrencyCode,
    decimal? Bid,
    decimal? Ask,
    decimal? LotSize,
    string? Unit,
    bool? ForceOpenAllowed,
    bool? StopsLimitsAllowed,
    bool? ControlledRiskAllowed,
    bool? StreamingPricesAvailable,
    MarketDealingRulesSummary? DealingRules,
    IReadOnlyList<string> SupportedOrderTypes);

public sealed record MarketDealingRulesSummary(
    MarketRuleDistanceSummary? MinimumDealSize,
    MarketRuleDistanceSummary? MinimumStepDistance,
    MarketRuleDistanceSummary? MinimumControlledRiskStopDistance,
    MarketRuleDistanceSummary? MinimumStopOrLimitDistance,
    MarketRuleDistanceSummary? MaximumStopOrLimitDistance,
    string? MarketOrderPreference,
    string? TrailingStopsPreference);

public sealed record MarketRuleDistanceSummary(decimal? Value, string? Unit);
