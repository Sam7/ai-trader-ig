namespace Trading.Strategy.Inputs;

public interface ITradingClock
{
    DateTimeOffset UtcNow { get; }
}
