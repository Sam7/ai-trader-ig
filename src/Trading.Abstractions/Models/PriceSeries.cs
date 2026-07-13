namespace Trading.Abstractions;

public sealed record PriceSeries(
    InstrumentId Instrument,
    PriceResolution? Resolution,
    IReadOnlyList<PriceBar> Bars,
    HistoricalPriceAllowance? Allowance = null);

/// <summary>Broker-reported budget for historical price retrieval.</summary>
public sealed record HistoricalPriceAllowance(int? Remaining, TimeSpan? ResetAfter);
