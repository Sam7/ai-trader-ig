using Trading.Abstractions;

namespace Trading.Automation.Execution;

public sealed record CachedPriceSeriesResult(
    PriceSeries Series,
    PriceSeriesRefreshMode RefreshMode,
    int FetchedBarCount);
