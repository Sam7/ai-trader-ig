using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using TickerQ.DependencyInjection;
using Trading.Automation.DependencyInjection;

namespace Trading.Automation;

public static class TradingWorkerApplication
{
    public static async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
        builder.Configuration.AddUserSecrets(typeof(TradingWorkerApplication).Assembly, optional: true);

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

        var app = builder.Build();
        app.UseTickerQ();
        await app.StartAsync(cancellationToken);
        await app.WaitForShutdownAsync(cancellationToken);
    }
}
