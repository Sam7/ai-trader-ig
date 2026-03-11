using Trading.Automation;

public interface IAutomationRuntime
{
    Task RunAsync(CancellationToken cancellationToken = default);
}

public sealed class AutomationRuntime : IAutomationRuntime
{
    public Task RunAsync(CancellationToken cancellationToken = default)
        => TradingWorkerApplication.RunAsync([], cancellationToken);
}
