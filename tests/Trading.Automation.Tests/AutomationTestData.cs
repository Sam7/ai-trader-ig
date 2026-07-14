using Trading.AI.DailyBriefing;
using Trading.Strategy.Inputs;
using Trading.Strategy.Shared;

internal static class AutomationTestData
{
    public static IntradayOpportunityReviewRequest CreateIntradayReviewRequest(
        DateOnly tradingDate,
        DateTimeOffset requestedAtUtc)
        => new(
            tradingDate,
            requestedAtUtc.AddMinutes(-60),
            requestedAtUtc,
            4,
            "Australia/Melbourne",
            new TradingDayPlan(
                tradingDate,
                "Daily plan",
                "Regime summary",
                MarketRegime.Mixed,
                [],
                [],
                [],
                requestedAtUtc.AddHours(-1)),
            [],
            requestedAtUtc);
}
