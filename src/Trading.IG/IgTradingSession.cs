using Trading.Abstractions;

namespace Trading.IG;

public sealed record IgTradingSession(
    string AccountId,
    DateTimeOffset AuthenticatedAtUtc) : ITradingSession
{
    public string BrokerName => "IG";
}
