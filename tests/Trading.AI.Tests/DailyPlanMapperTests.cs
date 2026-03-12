using FluentAssertions;
using Trading.AI.Configuration;
using Trading.AI.DailyBriefing;
using Trading.AI.Prompts.DailyPlanJson;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.Inputs;
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
            MarketRegime.Mixed,
            [
                CreateMarket("CC.D.WTI.UMA.IP", 1),
                CreateMarket("CC.D.LCO.UMA.IP", 2),
                CreateMarket("CS.D.GC.UMA.IP", 3),
            ],
            [],
            [],
            [],
            [
                new PlannedCalendarEventDocument(
                    "EVT-01",
                    "U.S. Weekly Petroleum Status Report",
                    DateTimeOffset.Parse("2026-03-12T14:30:00Z"),
                    "High",
                    ["CC.D.WTI.UMA.IP", "CC.D.LCO.UMA.IP"])
            ]);

        var trackedMarkets = new Dictionary<string, TrackedMarketOptions>(StringComparer.Ordinal)
        {
            ["CC.D.WTI.UMA.IP"] = new() { InstrumentId = "CC.D.WTI.UMA.IP", DisplayName = "WTI", Sector = "Energy" },
            ["CC.D.LCO.UMA.IP"] = new() { InstrumentId = "CC.D.LCO.UMA.IP", DisplayName = "Brent", Sector = "Energy" },
            ["CS.D.GC.UMA.IP"] = new() { InstrumentId = "CS.D.GC.UMA.IP", DisplayName = "Gold", Sector = "Metals" },
        };

        var plannedAtUtc = DateTimeOffset.Parse("2026-03-12T07:45:00Z");

        var plan = mapper.Map(document, request, trackedMarkets, plannedAtUtc);

        plan.PlannedAtUtc.Should().Be(plannedAtUtc);
        plan.MarketRegime.Should().Be(MarketRegime.Mixed);
        plan.WatchList.Should().HaveCount(3);
        plan.CalendarEvents.Should().ContainSingle();
        plan.CalendarEvents[0].Impact.Should().Be(EconomicEventImpact.High);
        plan.CalendarEvents[0].AffectedInstruments.Should().HaveCount(2);
    }

    private static PlannedMarketDocument CreateMarket(string instrumentId, int rank)
        => new(
            instrumentId,
            $"Name {rank}",
            rank,
            $"Rationale {rank}",
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
