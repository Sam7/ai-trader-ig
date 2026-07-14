using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.Abstractions;
using Trading.Automation.Configuration;
using Trading.Automation.Execution;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.Inputs;
using Trading.Strategy.Shared;

public sealed class DailyBriefingPlanServiceTests
{
    [Fact]
    public async Task RunAsync_ShouldPlanRequestedTradingDate()
    {
        var planner = new FakeTradingDayPlanner();
        var service = new DailyBriefingPlanService(
            planner,
            Options.Create(new AutomationOptions()),
            NullLogger<DailyBriefingPlanService>.Instance);

        var plan = await service.RunAsync(new DateOnly(2026, 3, 12));

        planner.Requests.Should().ContainSingle();
        planner.Requests[0].TradingDate.Should().Be(new DateOnly(2026, 3, 12));
        plan.TradingDate.Should().Be(new DateOnly(2026, 3, 12));
    }

    private sealed class FakeTradingDayPlanner : ITradingDayPlanner
    {
        public List<TradingDayRequest> Requests { get; } = [];

        public Task<TradingDayPlan> PlanAsync(TradingDayRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            var market = new MarketWatch(
                new InstrumentId("CC.D.WTI.UMA.IP"),
                1,
                "Momentum remains constructive.",
                new TradeScenario(TradeDirection.Buy, "Long thesis", "Breakout confirmation", "Range failure", [], null),
                new TradeScenario(TradeDirection.Sell, "Short thesis", "Breakdown confirmation", "Trend recovery", [], null));

            return Task.FromResult(new TradingDayPlan(
                request.TradingDate,
                "Macro summary",
                "Regime summary",
                MarketRegime.Mixed,
                [market],
                [market],
                [],
                DateTimeOffset.Parse("2026-03-12T08:00:00Z")));
        }

    }
}
