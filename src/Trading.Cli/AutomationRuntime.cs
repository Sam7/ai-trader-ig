using Trading.Automation;

public interface IAutomationRuntime
{
    Task RunAsync(
        TimeSpan? duration = null,
        IReadOnlyList<string>? instruments = null,
        string? observabilityRootPath = null,
        CancellationToken cancellationToken = default);
}

public sealed class AutomationRuntime : IAutomationRuntime
{
    public async Task RunAsync(
        TimeSpan? duration = null,
        IReadOnlyList<string>? instruments = null,
        string? observabilityRootPath = null,
        CancellationToken cancellationToken = default)
    {
        if (duration is null)
        {
            await TradingWorkerApplication.RunAsync([], cancellationToken, instruments ?? [], observabilityRootPath);
            return;
        }

        using var durationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        durationCancellation.CancelAfter(duration.Value);

        try
        {
            await TradingWorkerApplication.RunAsync([], durationCancellation.Token, instruments ?? [], observabilityRootPath);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && durationCancellation.IsCancellationRequested)
        {
            // A bounded run ending on its own timer is a successful completion.
        }
    }
}
