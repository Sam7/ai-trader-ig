using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

public sealed class DailyPlanEnsureServiceTests
{
    [Fact]
    public async Task EnsureAsync_ShouldReturnExistingPlanWithoutPlanningAgain()
    {
        var store = new InMemoryTradingDayStore();
        var tradingDate = new DateOnly(2026, 7, 3);
        var plan = CreatePlan(tradingDate);
        await store.SaveAsync(TradingDayRecord.StartNew(plan));
        var workflow = new FakeTradingDayWorkflow(store);
        var service = CreateService(store, workflow);

        var result = await service.EnsureAsync(tradingDate);

        result.Should().BeSameAs(plan);
        workflow.PlanRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureAsync_ShouldCreateMissingPlan()
    {
        var store = new InMemoryTradingDayStore();
        var workflow = new FakeTradingDayWorkflow(store);
        var service = CreateService(store, workflow);
        var tradingDate = new DateOnly(2026, 7, 3);

        var result = await service.EnsureAsync(tradingDate);

        result.TradingDate.Should().Be(tradingDate);
        workflow.PlanRequests.Should().ContainSingle().Which.TradingDate.Should().Be(tradingDate);
        var stored = await store.GetAsync(tradingDate);
        stored?.Plan.Should().Be(result);
    }

    [Fact]
    public async Task EnsureAsync_ShouldCreateMissingPlanOnlyOnceForConcurrentCalls()
    {
        var store = new InMemoryTradingDayStore();
        var workflow = new FakeTradingDayWorkflow(store)
        {
            PlanningDelay = TimeSpan.FromMilliseconds(50),
        };
        var service = CreateService(store, workflow);
        var tradingDate = new DateOnly(2026, 7, 3);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.EnsureAsync(tradingDate)));

        results.Should().OnlyContain(plan => plan.TradingDate == tradingDate);
        workflow.PlanRequests.Should().ContainSingle();
    }

    private static DailyPlanEnsureService CreateService(
        ITradingDayStore store,
        FakeTradingDayWorkflow workflow)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITradingDayWorkflow>(workflow);
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

    private static TradingDayPlan CreatePlan(DateOnly tradingDate)
    {
        var market = new MarketWatch(
            new InstrumentId("CC.D.CL.UMA.IP"),
            1,
            "Momentum remains constructive.",
            new TradeScenario(TradeDirection.Buy, "Long thesis", "Breakout confirmation", "Range failure", [], null),
            new TradeScenario(TradeDirection.Sell, "Short thesis", "Breakdown confirmation", "Trend recovery", [], null));

        return new TradingDayPlan(
            tradingDate,
            "Macro summary",
            "Regime summary",
            MarketRegime.Mixed,
            [market],
            [market],
            [],
            DateTimeOffset.Parse("2026-07-03T08:00:00Z"));
    }

    private sealed class FakeTradingDayWorkflow : ITradingDayWorkflow
    {
        private readonly ITradingDayStore _store;

        public FakeTradingDayWorkflow(ITradingDayStore store)
        {
            _store = store;
        }

        public List<TradingDayRequest> PlanRequests { get; } = [];

        public TimeSpan PlanningDelay { get; init; }

        public async Task<TradingDayPlan> PlanTradingDayAsync(
            TradingDayRequest request,
            CancellationToken cancellationToken = default)
        {
            PlanRequests.Add(request);
            if (PlanningDelay > TimeSpan.Zero)
            {
                await Task.Delay(PlanningDelay, cancellationToken);
            }

            var plan = CreatePlan(request.TradingDate);
            await _store.SaveAsync(TradingDayRecord.StartNew(plan), cancellationToken);
            return plan;
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
