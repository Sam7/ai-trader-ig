using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.Abstractions;
using Trading.Automation.Configuration;
using Trading.Automation.Execution;
using Trading.Strategy.ActiveTradeManagement;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.Inputs;
using Trading.Strategy.MarketAttention;
using Trading.Strategy.OpportunityReview;
using Trading.Strategy.Persistence;
using Trading.Strategy.Shared;
using Trading.Strategy.Workflow;

public sealed class IntradayOpportunityScanServiceTests
{
    [Fact]
    public async Task RunAsync_WhenDailyPlanCreationFails_ShouldReturnNull()
    {
        var store = new InMemoryTradingDayStore();
        var workflow = new ThrowingPlanWorkflow();
        var ensureService = CreateEnsureService(store, workflow);
        var service = CreateService(store, ensureService);

        var result = await service.RunAsync(
            new DateOnly(2026, 7, 3),
            DateTimeOffset.Parse("2026-07-03T01:00:00Z"));

        result.Should().BeNull();
        workflow.PlanRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task PrepareAsync_WhenDailyPlanIsMissing_ShouldNotCreateDailyPlan()
    {
        var store = new InMemoryTradingDayStore();
        var workflow = new ThrowingPlanWorkflow();
        var ensureService = CreateEnsureService(store, workflow);
        var service = CreateService(store, ensureService);

        var result = await service.PrepareAsync(
            new DateOnly(2026, 7, 3),
            DateTimeOffset.Parse("2026-07-03T01:00:00Z"));

        result.Should().BeNull();
        workflow.PlanRequests.Should().BeEmpty();
    }

    private static IntradayOpportunityScanService CreateService(
        ITradingDayStore store,
        DailyPlanEnsureService ensureService)
        => new(
            store,
            priceSeriesCache: null!,
            priceChartRenderer: null!,
            intradayOpportunityReviewer: null!,
            preparationWriter: null!,
            decisionAuditWriter: null!,
            ensureService,
            workflow: null!,
            Options.Create(new AutomationOptions()),
            Options.Create(new DailyBriefingOptions()),
            NullLogger<IntradayOpportunityScanService>.Instance);

    private static DailyPlanEnsureService CreateEnsureService(
        ITradingDayStore store,
        ITradingDayWorkflow workflow)
    {
        var services = new ServiceCollection();
        services.AddSingleton(workflow);
        services.AddSingleton(Options.Create(new AutomationOptions()));
        services.AddSingleton<ILogger<DailyBriefingPlanService>>(NullLogger<DailyBriefingPlanService>.Instance);
        services.AddTransient<DailyBriefingPlanService>();
        var provider = services.BuildServiceProvider();

        return new DailyPlanEnsureService(
            store,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AutomationOptions()),
            NullLogger<DailyPlanEnsureService>.Instance);
    }

    private sealed class ThrowingPlanWorkflow : ITradingDayWorkflow
    {
        public List<TradingDayRequest> PlanRequests { get; } = [];

        public Task<TradingDayPlan> PlanTradingDayAsync(
            TradingDayRequest request,
            CancellationToken cancellationToken = default)
        {
            PlanRequests.Add(request);
            throw new InvalidOperationException("Daily plan failed.");
        }

        public Task<IntradayOpportunityReviewResult> ReviewIntradayOpportunitiesAsync(
            IntradayOpportunityBatch batch,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MarketAssessment> AssessMarketAsync(MarketEvent marketEvent, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OpportunityReviewResult> ReviewOpportunityAsync(ReviewMarketUpdate review, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ActiveTradeDecision> ReviewActiveTradeAsync(ActiveTradeReviewRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TradingDayStatus> ApplyExecutionReportAsync(ExecutionReport report, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
