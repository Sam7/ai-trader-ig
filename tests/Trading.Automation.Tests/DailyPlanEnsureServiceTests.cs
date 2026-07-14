using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.Abstractions;
using Trading.Automation.Configuration;
using Trading.Automation.Execution;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.Inputs;
using Trading.Strategy.Persistence;
using Trading.Strategy.Shared;

public sealed class DailyPlanEnsureServiceTests
{
    [Fact]
    public async Task EnsureAsync_ShouldReturnExistingPlanWithoutPlanningAgain()
    {
        var store = new InMemoryTradingDayStore();
        var tradingDate = new DateOnly(2026, 7, 3);
        var plan = CreatePlan(tradingDate);
        await store.SaveAsync(TradingDayRecord.StartNew(plan));
        var planner = new FakeTradingDayPlanner(store);
        var service = CreateService(store, planner);

        var result = await service.EnsureAsync(tradingDate);

        result.Should().BeSameAs(plan);
        planner.PlanRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureAsync_ShouldCreateMissingPlan()
    {
        var store = new InMemoryTradingDayStore();
        var planner = new FakeTradingDayPlanner(store);
        var service = CreateService(store, planner);
        var tradingDate = new DateOnly(2026, 7, 3);

        var result = await service.EnsureAsync(tradingDate);

        result.TradingDate.Should().Be(tradingDate);
        planner.PlanRequests.Should().ContainSingle().Which.TradingDate.Should().Be(tradingDate);
        var stored = await store.GetAsync(tradingDate);
        stored?.Plan.Should().Be(result);
    }

    [Fact]
    public async Task EnsureAsync_ShouldCreateMissingPlanOnlyOnceForConcurrentCalls()
    {
        var store = new InMemoryTradingDayStore();
        var planner = new FakeTradingDayPlanner(store)
        {
            PlanningDelay = TimeSpan.FromMilliseconds(50),
        };
        var service = CreateService(store, planner);
        var tradingDate = new DateOnly(2026, 7, 3);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.EnsureAsync(tradingDate)));

        results.Should().OnlyContain(plan => plan.TradingDate == tradingDate);
        planner.PlanRequests.Should().ContainSingle();
    }

    private static DailyPlanEnsureService CreateService(
        ITradingDayStore store,
        FakeTradingDayPlanner planner)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITradingDayPlanner>(planner);
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

    private sealed class FakeTradingDayPlanner : ITradingDayPlanner
    {
        private readonly ITradingDayStore _store;

        public FakeTradingDayPlanner(ITradingDayStore store)
        {
            _store = store;
        }

        public List<TradingDayRequest> PlanRequests { get; } = [];

        public TimeSpan PlanningDelay { get; init; }

        public async Task<TradingDayPlan> PlanAsync(
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

    }
}
