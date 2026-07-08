namespace Trading.Execution;

public interface IExecutionClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemExecutionClock : IExecutionClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
