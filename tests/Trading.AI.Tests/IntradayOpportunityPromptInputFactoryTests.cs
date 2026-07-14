using FluentAssertions;
using Trading.Abstractions;
using Trading.AI.DailyBriefing;
using Trading.Strategy.Inputs;
using Trading.Strategy.Shared;

public sealed class IntradayOpportunityPromptInputFactoryTests
{
    [Fact]
    public void Create_should_format_typed_plan_market_and_event_context()
    {
        var request = CreateRequest();

        var input = IntradayOpportunityPromptInputFactory.Create(request);

        input.WatchedMarketCount.Should().Be(1);
        input.DailyPlanSummary.Should().Contain("Market regime: Mixed");
        input.WatchedMarketsContext.Should().Contain("## Rank 1: EUR/USD");
        input.WatchedMarketsContext.Should().Contain("Current mid price: 1.1");
        input.CalendarEventsContext.Should().Contain("evt-1");
        input.CalendarEventsContext.Should().Contain("CS.D.EURUSD.CFD.IP");
    }

    private static IntradayOpportunityReviewRequest CreateRequest()
    {
        var instrument = new InstrumentId("CS.D.EURUSD.CFD.IP");
        var longScenario = new TradeScenario(TradeDirection.Buy, "Long thesis", "Long confirmation", "Long invalidation", [], null);
        var shortScenario = new TradeScenario(TradeDirection.Sell, "Short thesis", "Short confirmation", "Short invalidation", [], null);
        var watch = new MarketWatch(instrument, 1, "Daily rationale", longScenario, shortScenario);
        var plan = new TradingDayPlan(
            new DateOnly(2026, 7, 3),
            "Macro summary",
            "Regime summary",
            MarketRegime.Mixed,
            [watch],
            [watch],
            [new EconomicEvent(
                "evt-1",
                "Payrolls",
                DateTimeOffset.Parse("2026-07-03T12:30:00Z"),
                EconomicEventImpact.High,
                [instrument])],
            DateTimeOffset.Parse("2026-07-03T00:00:00Z"));

        return new IntradayOpportunityReviewRequest(
            new DateOnly(2026, 7, 3),
            DateTimeOffset.Parse("2026-07-03T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-03T01:00:00Z"),
            1,
            "Australia/Sydney",
            plan,
            [new IntradayMarketReviewContext(
                instrument,
                "EUR/USD",
                1,
                "Daily rationale",
                longScenario,
                shortScenario,
                1.0999m,
                1.1001m,
                1.1000m,
                0.0002m,
                DateTimeOffset.Parse("2026-07-03T00:59:00Z"))],
            DateTimeOffset.Parse("2026-07-03T01:00:00Z"));
    }
}
