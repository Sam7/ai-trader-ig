using Trading.Abstractions;

namespace Trading.Strategy.Shared;

public sealed record IntradayMarketAssessment(
    InstrumentId Instrument,
    string InstrumentName,
    int OpportunityScore,
    TradeDirection DirectionalBias,
    string Summary,
    string WhyNow,
    string StandAsideReason)
{
    public void Validate()
    {
        if (OpportunityScore is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(OpportunityScore), "Opportunity score must be between 0 and 100.");
        }

        if (string.IsNullOrWhiteSpace(InstrumentName))
        {
            throw new ArgumentException("Instrument name is required.", nameof(InstrumentName));
        }
    }
}
