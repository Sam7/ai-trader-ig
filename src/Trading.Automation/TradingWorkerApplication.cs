using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using TickerQ.DependencyInjection;
using Trading.AI.Configuration;
using Trading.Automation.Configuration;
using Trading.Automation.DependencyInjection;
using Trading.Charting.DependencyInjection;
using Trading.IG.DependencyInjection;
using Trading.MarketData;

namespace Trading.Automation;

public static class TradingWorkerApplication
{
    public static async Task RunAsync(
        string[] args,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? trackedMarketInstrumentFilter = null,
        string? observabilityRootPath = null)
    {
        var app = BuildApplication(args, trackedMarketInstrumentFilter, observabilityRootPath);
        app.UseTickerQ();
        await app.StartAsync(cancellationToken);
        await app.WaitForShutdownAsync(cancellationToken);
    }

    public static async Task<int> RunMaintenanceAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length != 2 || !string.Equals(args[0], "--create-deployment-checkpoint", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Usage: Trading.Worker --create-deployment-checkpoint <deployment-id>");
            return 2;
        }

        var app = BuildApplication([], null, null);
        await using (app)
        {
            using var scope = app.Services.CreateScope();
            var briefing = scope.ServiceProvider.GetRequiredService<IOptions<DailyBriefingOptions>>().Value;
            var collector = scope.ServiceProvider.GetRequiredService<IOptions<MarketDataCollectorOptions>>().Value;
            var instruments = briefing.TrackedMarkets
                .Select(market => market.InstrumentId)
                .Where(instrument => !string.IsNullOrWhiteSpace(instrument))
                .Select(instrument => new Trading.Abstractions.InstrumentId(instrument))
                .Distinct()
                .ToArray();
            var continuity = scope.ServiceProvider.GetRequiredService<MarketDataDeploymentContinuityService>();
            var checkpoint = await continuity.CreateCheckpointAsync(args[1], instruments, collector.Resolution, cancellationToken);
            Console.WriteLine($"Created deployment continuity checkpoint {checkpoint.DeploymentId} for {checkpoint.Markets.Count} market(s).");
            return 0;
        }
    }

    private static WebApplication BuildApplication(
        string[] args,
        IReadOnlyList<string>? trackedMarketInstrumentFilter,
        string? observabilityRootPath)
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

        return builder.Build();
    }
}
