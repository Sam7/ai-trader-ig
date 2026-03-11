using FluentAssertions;
using Trading.AI.Configuration;
using Trading.AI.DailyBriefing;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.Rules;

public sealed class DailyPlanMapperTests
{
    [Fact]
    public void Map_ShouldUseProvidedPlannedAtTimestamp()
    {
        var mapper = new DailyPlanMapper();
        var request = new DailyBriefingRequest(
            new TradingDayRequest(new DateOnly(2026, 3, 12)),
            StrategyRules.Default,
            DateTimeOffset.Parse("2026-03-12T07:30:00Z"));
        var document = new DailyPlanDocument(
            "Macro",
            "Mixed regime",
            "Mixed",
            [
                CreateMarket("CC.D.WTI.UMA.IP", 1),
            ],
            [
                CreateMarket("CC.D.WTI.UMA.IP", 1),
                CreateMarket("CC.D.LCO.UMA.IP", 2),
                CreateMarket("CS.D.GC.UMA.IP", 3),
            ],
            []);

        var trackedMarkets = new Dictionary<string, TrackedMarketOptions>(StringComparer.Ordinal)
        {
            ["CC.D.WTI.UMA.IP"] = new() { InstrumentId = "CC.D.WTI.UMA.IP", DisplayName = "WTI", Sector = "Energy" },
            ["CC.D.LCO.UMA.IP"] = new() { InstrumentId = "CC.D.LCO.UMA.IP", DisplayName = "Brent", Sector = "Energy" },
            ["CS.D.GC.UMA.IP"] = new() { InstrumentId = "CS.D.GC.UMA.IP", DisplayName = "Gold", Sector = "Metals" },
        };

        var plannedAtUtc = DateTimeOffset.Parse("2026-03-12T07:45:00Z");

        var plan = mapper.Map(document, request, trackedMarkets, plannedAtUtc);

        plan.PlannedAtUtc.Should().Be(plannedAtUtc);
    }

    private static PlannedMarketDocument CreateMarket(string instrumentId, int rank)
        => new(
            instrumentId,
            rank,
            $"Rationale {rank}",
            10m + rank,
            11m + rank,
            new PlannedTradeScenarioDocument(
                "Long thesis",
                "Long confirmation",
                "Long invalidation",
                [],
                null),
            new PlannedTradeScenarioDocument(
                "Short thesis",
                "Short confirmation",
                "Short invalidation",
                [],
                null));
}
