using Trading.Abstractions;

namespace Trading.Strategy.Shared;

public sealed record TradeSetup(
    InstrumentId Instrument,
    TradeDirection Direction,
    decimal EntryPrice,
    decimal StopPrice,
    decimal TargetPrice,
    decimal Confidence,
    string Catalyst,
    string Thesis,
    string Invalidation,
    DateTimeOffset PlannedAtUtc)
{
    public void Validate()
    {
        if (Confidence < 0m || Confidence > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(Confidence), "Confidence must be between zero and one.");
        }

        if (EntryPrice <= 0m || StopPrice <= 0m || TargetPrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(EntryPrice), "Entry, stop, and target prices must be greater than zero.");
        }

        if (Direction == TradeDirection.Buy && (StopPrice >= EntryPrice || TargetPrice <= EntryPrice))
        {
            throw new ArgumentException("Buy setups require stop below entry and target above entry.");
        }

        if (Direction == TradeDirection.Sell && (StopPrice <= EntryPrice || TargetPrice >= EntryPrice))
        {
            throw new ArgumentException("Sell setups require stop above entry and target below entry.");
        }
    }
}
