namespace Trading.Automation.Execution;

public sealed class IntradayOpportunityScanGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool TryEnter() => _gate.Wait(0);

    public void Release() => _gate.Release();
}
