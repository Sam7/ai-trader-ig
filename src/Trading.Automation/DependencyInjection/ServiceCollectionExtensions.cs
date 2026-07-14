using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trading.AI.DependencyInjection;
using Trading.Automation.Configuration;
using Trading.Automation.Diagnostics;
using Trading.Automation.Execution;
using Trading.Automation.Health;
using Trading.Automation.MarketData;
using Trading.Automation.Scheduling;
using Trading.Execution;
using Trading.MarketData.DependencyInjection;
using Trading.Strategy.DependencyInjection;
using Trading.Strategy.Inputs;

namespace Trading.Automation.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTradingAutomation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AutomationOptions>()
            .Bind(configuration.GetSection(AutomationOptions.SectionName));
        services.AddOptions<MarketDataCollectionOptions>()
            .Bind(configuration.GetSection($"{AutomationOptions.SectionName}:MarketDataCollection"));
        services.AddOptions<WorkerHealthOptions>()
            .Bind(configuration.GetSection(WorkerHealthOptions.SectionName));
        services.AddOptions<AlertingOptions>()
            .Bind(configuration.GetSection(AlertingOptions.SectionName));
        services.AddSingleton<WorkerOperationMetrics>();

        services.AddTradingAi(configuration);
        services.AddTradingMarketData(configuration);
        services.AddWorkerDiagnostics(configuration);
        services.UseGcsWorkerDiagnosticsArtifactUploader();
        services.AddTradingStrategyCore();
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AutomationOptions>>().Value;
            return options.Execution.CreateShadowDecisionPolicy(options.Timezone);
        });

        services.AddSingleton<SystemTradingClock>();
        services.AddSingleton<ITradingClock>(sp => sp.GetRequiredService<SystemTradingClock>());
        services.AddSingleton<IExecutionClock, SystemExecutionClock>();
        services.AddSingleton<IExecutionDealReferenceFactory, ExecutionDealReferenceFactory>();
        services.AddSingleton<IExecutionBoundaryStore>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AutomationOptions>>().Value;
            return new SqliteExecutionBoundaryStore(options.Execution.StorePath);
        });
        services.AddSingleton<ExecutionBoundaryService>();
        services.AddSingleton<ExecutionSubmissionService>();
        services.AddSingleton<IExecutionSubmissionService>(sp => sp.GetRequiredService<ExecutionSubmissionService>());
        services.AddTransient<DemoCanaryExecutionService>();

        services.AddTransient<DailyBriefingResearchService>();
        services.AddTransient<DailyBriefingPlanService>();
        services.AddSingleton<DailyPlanEnsureService>();
        services.AddSingleton<IntradayOpportunityScanGate>();
        services.AddSingleton<IntradayPriceSeriesCache>();
        services.AddSingleton<IIntradayPriceSeriesSource>(
            sp => sp.GetRequiredService<IntradayPriceSeriesCache>());
        services.AddSingleton<IntradayOpportunityPreparationWriter>();
        services.AddSingleton<IIntradayOpportunityPreparationStore>(
            sp => sp.GetRequiredService<IntradayOpportunityPreparationWriter>());
        services.AddTransient<IntradayOpportunityAnalysisService>();
        services.AddTransient<IIntradayOpportunityRequestRenderer>(
            sp => sp.GetRequiredService<IntradayOpportunityAnalysisService>());
        services.AddTransient<IIntradayOpportunityAnalysisService>(
            sp => sp.GetRequiredService<IntradayOpportunityAnalysisService>());
        services.AddTransient<IIntradayOpportunityPreparationService, IntradayOpportunityPreparationService>();
        services.AddTransient<IIntradayOpportunityDecisionCoordinator, IntradayOpportunityDecisionCoordinator>();
        services.AddSingleton<DecisionAuditWriter>();
        services.AddSingleton<DecisionEvidenceSidecarWriter>();
        services.AddSingleton<PaperTradeOutcomeEvaluator>();
        services.AddSingleton<PaperMarketAssessmentEvaluator>();
        services.AddSingleton<AuditMarketDataQualityAnalyzer>();
        services.AddTransient<IDecisionAuditEvaluationService, DecisionAuditEvaluationService>();
        services.AddTransient<IntradayOpportunityScanService>();
        services.AddTransient<DailyBriefingTickerJob>();
        services.AddTransient<IntradayOpportunityTickerJob>();
        services.AddHttpClient<SlackAlertService>();
        services.AddHostedService<MarketDataCollectionHostedService>();
        services.AddHostedService<MarketDataRecoveryHostedService>();
        services.AddHostedService<MarketDataSnapshotPublisherHostedService>();
        services.AddHostedService<MarketDataSnapshotMirrorHostedService>();
        services.AddHostedService<WorkerHealthReporterHostedService>();
        services.AddHostedService<DailyBriefingScheduleInitializer>();
        services.AddHostedService<IntradayOpportunityScheduleInitializer>();
        return services;
    }
}
