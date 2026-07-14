using FluentAssertions;
using Trading.Abstractions;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.Inputs;
using Trading.Strategy.OpportunityReview;
using Trading.Strategy.Persistence;
using Trading.Strategy.Shared;

namespace Trading.Strategy.Tests;

public sealed class TradingDayServicesTests
{
    private static readonly DateOnly TradingDate = new(2026, 3, 11);
    private static readonly DateTimeOffset NowUtc = DateTimeOffset.Parse("2026-03-11T06:00:00Z");

    [Fact]
    public async Task Planner_should_save_the_policy_sized_daily_plan()
    {
        var store = new InMemoryTradingDayStore();
        ITradingDayPlanner planner = new TradingDayPlanner(
            DailyPlanningPolicy.Default,
            new FakeDailyBriefingComposer(CreatePlan()),
            new FakeTradingClock(NowUtc),
            store);

        var plan = await planner.PlanAsync(new TradingDayRequest(TradingDate));
        var record = await store.GetAsync(TradingDate);

        plan.WatchList.Should().HaveCount(DailyPlanningPolicy.Default.ShortlistSize);
        record?.Plan.Should().BeSameAs(plan);
    }

    [Fact]
    public async Task Intraday_decision_service_should_review_candidates_for_watched_markets()
    {
        var store = new InMemoryTradingDayStore();
        await store.SaveAsync(TradingDayRecord.StartNew(CreatePlan()));
        IIntradayDecisionService service = new IntradayOpportunityReviewService(
            store,
            new IntradayCandidateDecisionService(ShadowDecisionPolicy.Disabled()));

        var result = await service.ReviewAsync(CreateBatch());

        result.CandidateOpportunities.Should().ContainSingle();
        result.CandidateDecisions.Should().ContainSingle();
        result.CandidateDecisions[0].Reasons.Should().Contain(IntradayCandidateDecisionReason.ExecutionDisabled);
    }

    private static TradingDayPlan CreatePlan()
    {
        var watchedMarkets = new[]
        {
            CreateMarketWatch("CS.D.EURUSD.CFD.IP", 1),
            CreateMarketWatch("CC.D.GOLD.UMA.IP", 2),
            CreateMarketWatch("CS.D.USDJPY.CFD.IP", 3),
        };
        return new TradingDayPlan(
            TradingDate,
            "Macro calm with USD in focus.",
            "Mixed but selective.",
            MarketRegime.Mixed,
            watchedMarkets,
            watchedMarkets,
            [],
            NowUtc);
    }

    private static MarketWatch CreateMarketWatch(string instrument, int rank)
        => new(
            new InstrumentId(instrument),
            rank,
            $"Ranked #{rank}",
            new TradeScenario(TradeDirection.Buy, "Long thesis", "Breakout holds", "Breaks support", [], null),
            new TradeScenario(TradeDirection.Sell, "Short thesis", "Breakdown holds", "Reclaims resistance", [], null));

    private static IntradayOpportunityBatch CreateBatch()
        => new(
            TradingDate,
            NowUtc,
            NowUtc.AddMinutes(-60),
            NowUtc,
            [new IntradayMarketAssessment(
                new InstrumentId("CS.D.EURUSD.CFD.IP"),
                "EUR/USD",
                74,
                TradeDirection.Buy,
                "Clean continuation structure.",
                "USD softness and stable spread.",
                string.Empty)],
            [new IntradayOpportunityCandidate(
                new InstrumentId("CS.D.EURUSD.CFD.IP"),
                "EUR/USD",
                TradeDirection.Buy,
                78,
                TradeEntryMethod.Limit,
                1.1000m,
                1.0975m,
                1.1050m,
                2.0m,
                1.1002m,
                0.0002m,
                "Buy pullback into support.",
                "Breaks back below intraday support.",
                "Momentum and structure align now.",
                NowUtc.AddMinutes(45))]);

    private sealed class FakeDailyBriefingComposer(TradingDayPlan plan) : IDailyBriefingComposer
    {
        public Task<TradingDayPlan> ComposeAsync(DailyBriefingRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(plan);
    }

    private sealed class FakeTradingClock(DateTimeOffset utcNow) : ITradingClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
