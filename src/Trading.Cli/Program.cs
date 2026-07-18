using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using Trading.Automation.Configuration;
using Trading.Automation.DependencyInjection;
using Trading.Charting.DependencyInjection;
using Trading.IG.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
// Re-apply environment variables after the optional local file so bounded local
// runs can override ignored machine-local paths without changing that file.
builder.Configuration.AddEnvironmentVariables();
TrackedMarketsConfiguration.AddConfiguredTrackedMarketsFile(builder.Configuration);
builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.AddIgTradingGateway(builder.Configuration);
builder.Services.AddTradingAutomation(builder.Configuration);
builder.Services.AddTradingCharting();
builder.Services.AddSingleton<IAnsiConsole>(AnsiConsole.Console);
builder.Services.AddTradingCli();

var application = new TradingCliApplication(builder.Services, AnsiConsole.Console);
using var cancellationSource = new CancellationTokenSource();

Console.CancelKeyPress += OnCancelKeyPress;

try
{
    return await application.RunAsync(args, cancellationSource.Token);
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
