using Trading.Automation;

using var cancellationSource = new CancellationTokenSource();

Console.CancelKeyPress += OnCancelKeyPress;

try
{
    await TradingWorkerApplication.RunAsync(args, cancellationSource.Token);
}
finally
{
    Console.CancelKeyPress -= OnCancelKeyPress;
}

void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs eventArgs)
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
}
