using Trading.Automation;

using var cancellationSource = new CancellationTokenSource();

Console.CancelKeyPress += OnCancelKeyPress;

try
{
    if (args.FirstOrDefault() == "--create-deployment-checkpoint")
    {
        Environment.ExitCode = await TradingWorkerApplication.RunMaintenanceAsync(args, cancellationSource.Token);
    }
    else
    {
        await TradingWorkerApplication.RunAsync(args, cancellationSource.Token);
    }
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
