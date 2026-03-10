namespace Trading.Abstractions;

public sealed record PriceBar(
    DateTimeOffset TimestampUtc,
    decimal BidOpen,
    decimal BidHigh,
    decimal BidLow,
    decimal BidClose,
    decimal AskOpen,
    decimal AskHigh,
    decimal AskLow,
    decimal AskClose,
    long? Volume);
