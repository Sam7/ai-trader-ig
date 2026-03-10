using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using Trading.IG;
using Trading.IG.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.AddIgTradingGateway(builder.Configuration);
builder.Services.AddSingleton<IOrderReferenceJournal, FileOrderReferenceJournal>();
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
