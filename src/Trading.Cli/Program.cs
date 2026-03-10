using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trading.IG;
using Trading.IG.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.AddIgTradingGateway(builder.Configuration);
builder.Services.AddSingleton<IOrderReferenceJournal, FileOrderReferenceJournal>();
builder.Services.AddTransient<CliRunner>();

using var host = builder.Build();
var runner = host.Services.GetRequiredService<CliRunner>();
return await runner.RunAsync(args);
