namespace Trading.Abstractions;

public sealed record PriceSeries(
    InstrumentId Instrument,
    PriceResolution? Resolution,
    IReadOnlyList<PriceBar> Bars);
