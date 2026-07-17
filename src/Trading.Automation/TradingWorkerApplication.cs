using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using TickerQ.DependencyInjection;
using Trading.Automation.Configuration;
using Trading.Automation.DependencyInjection;
using Trading.Charting.DependencyInjection;
using Trading.IG.DependencyInjection;

namespace Trading.Automation;

public static class TradingWorkerApplication
{
    public static async Task RunAsync(
        string[] args,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? trackedMarketInstrumentFilter = null,
        string? observabilityRootPath = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
        TrackedMarketsConfiguration.AddConfiguredTrackedMarketsFile(builder.Configuration);
        TrackedMarketsConfiguration.ApplyTrackedMarketsOverride(builder.Configuration, trackedMarketInstrumentFilter ?? []);
        builder.Configuration.AddUserSecrets(typeof(TradingWorkerApplication).Assembly, optional: true);
        // Keep deploy-time environment overrides authoritative over optional local/user settings.
        builder.Configuration.AddEnvironmentVariables();
        PromptObservabilityConfiguration.ApplyRootOverride(builder.Configuration, observabilityRootPath);

        builder.Host.UseSerilog((context, services, loggerConfiguration) =>
        {
            loggerConfiguration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .WriteTo.Console();
        });

        var schedulerTimezoneId = builder.Configuration["Automation:Timezone"] ?? "Australia/Melbourne";
        builder.Services.AddTickerQ(options =>
        {
            options.ConfigureScheduler(scheduler =>
            {
                scheduler.SchedulerTimeZone = TimeZoneInfo.FindSystemTimeZoneById(schedulerTimezoneId);
            });
        });
        builder.Services.AddTradingAutomation(builder.Configuration);
        builder.Services.AddIgTradingGateway(builder.Configuration);
        builder.Services.AddTradingCharting();

        var app = builder.Build();
        app.UseTickerQ();
        await app.StartAsync(cancellationToken);
        await app.WaitForShutdownAsync(cancellationToken);
    }
}
