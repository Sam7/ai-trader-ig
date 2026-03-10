namespace Trading.Abstractions;

public interface ITradingSession
{
    string AccountId { get; }

    string BrokerName { get; }

    DateTimeOffset AuthenticatedAtUtc { get; }
}
