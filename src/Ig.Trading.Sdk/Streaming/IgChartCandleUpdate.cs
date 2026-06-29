namespace Ig.Trading.Sdk.Streaming;

public sealed record IgChartCandleUpdate(
    string Epic,
    string Scale,
    DateTimeOffset TimestampUtc,
    decimal BidOpen,
    decimal BidHigh,
    decimal BidLow,
    decimal BidClose,
    decimal OfferOpen,
    decimal OfferHigh,
    decimal OfferLow,
    decimal OfferClose,
    bool IsComplete,
    long? TickCount);
