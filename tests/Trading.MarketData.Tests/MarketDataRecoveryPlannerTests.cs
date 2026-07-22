using FluentAssertions;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.MarketData.Tests;

public sealed class MarketDataRecoveryPlannerTests
{
    [Fact]
    public async Task PlanRecentAsync_QueuesOnlyTheBoundedRecentGap()
    {
        var now = DateTimeOffset.Parse("2026-07-13T12:00:00Z");
        var store = new InMemoryMarketDataStore();
        var instrument = new InstrumentId("GOLD");
        await store.UpsertAsync(
        [
            Bar(instrument, now.AddMinutes(-20)),
            Bar(instrument, now.AddMinutes(-10)),
            Bar(instrument, now.AddMinutes(-5)),
        ]);
        var planner = new MarketDataRecoveryPlanner(
            store,
            store,
            store,
            new FixedMarketDataClock(now),
            new MarketDataRecoveryOptions { RecentLookback = TimeSpan.FromMinutes(20) });

        await planner.PlanRecentAsync([new MarketDataRecoveryTarget(instrument, 2)], PriceResolution.FiveMinutes);

        (await store.GetRecoveryWorkItemsAsync()).Should().ContainSingle().Which.Should().BeEquivalentTo(
            new MarketDataRecoveryWorkItem(
                instrument,
                PriceResolution.FiveMinutes,
                MarketDataRecoveryReason.RecentTail,
                2,
                now.AddMinutes(-15),
                now.AddMinutes(-10),
                now.AddMinutes(-15),
                MarketDataRecoveryWorkStatus.Pending,
                now,
                0,
                0));
    }

    [Fact]
    public async Task PlanRecentAsync_DoesNotQueueKnownClosedMarket()
    {
        var now = DateTimeOffset.Parse("2026-07-13T12:00:00Z");
        var store = new InMemoryMarketDataStore();
        var instrument = new InstrumentId("GOLD");
        await store.UpsertSessionStatusAsync(new MarketSessionStatusRecord(
            instrument,
            MarketStatus.Closed,
            now.AddMinutes(-1),
            now.AddMinutes(10),
            MarketSessionEvidenceSource.BrokerSnapshot));
        var planner = new MarketDataRecoveryPlanner(
            store,
            store,
            store,
            new FixedMarketDataClock(now),
            new MarketDataRecoveryOptions { RecentLookback = TimeSpan.FromMinutes(20) });

        await planner.PlanRecentAsync([new MarketDataRecoveryTarget(instrument, 1)], PriceResolution.FiveMinutes);

        (await store.GetRecoveryWorkItemsAsync()).Should().BeEmpty();
    }

    private static StoredPriceBar Bar(InstrumentId instrument, DateTimeOffset timestampUtc)
        => StoredPriceBar.FromPriceBar(
            instrument,
            PriceResolution.FiveMinutes,
            new PriceBar(timestampUtc, 1, 1, 1, 1, 1, 1, 1, 1, null),
            MarketDataSource.Stream);
}
