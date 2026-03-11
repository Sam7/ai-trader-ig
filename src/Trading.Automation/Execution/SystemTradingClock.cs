using Trading.Strategy.Inputs;

namespace Trading.Automation.Execution;

public sealed class SystemTradingClock : ITradingClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
