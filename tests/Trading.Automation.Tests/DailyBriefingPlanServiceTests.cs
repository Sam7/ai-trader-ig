using FluentAssertions;
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
using Trading.Strategy.Shared;
using Trading.Strategy.Workflow;

public sealed class DailyBriefingPlanServiceTests
{
    [Fact]
    public async Task RunAsync_ShouldPlanRequestedTradingDate()
    {
        var workflow = new FakeTradingDayWorkflow();
        var service = new DailyBriefingPlanService(
            workflow,
            Options.Create(new AutomationOptions()),
            NullLogger<DailyBriefingPlanService>.Instance);

        var plan = await service.RunAsync(new DateOnly(2026, 3, 12));

        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].TradingDate.Should().Be(new DateOnly(2026, 3, 12));
        plan.TradingDate.Should().Be(new DateOnly(2026, 3, 12));
    }

    private sealed class FakeTradingDayWorkflow : ITradingDayWorkflow
    {
        public List<TradingDayRequest> Requests { get; } = [];

        public Task<TradingDayPlan> PlanTradingDayAsync(TradingDayRequest request, CancellationToken cancellationToken = default)
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

        public Task<IntradayOpportunityReviewResult> ReviewIntradayOpportunitiesAsync(IntradayOpportunityBatch batch, CancellationToken cancellationToken = default)
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
