using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.MarketData.Tests;

public sealed class MarketDataStreamBatchIngestorTests
{
    [Fact]
    public async Task DisposeAsync_ShouldCoalesceFormingUpdatesBeforePersisting()
    {
        var store = new InMemoryMarketDataStore();
        var healthStore = new InMemoryMarketDataHealthStore();
        var metrics = new MarketDataStreamPipelineMetrics();
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        await using var ingestor = new MarketDataStreamBatchIngestor(
            store,
            healthStore,
            new FixedMarketDataClock(DateTimeOffset.Parse("2026-07-12T00:00:00Z")),
            new MarketDataCollectorOptions(),
            new MarketDataStreamIngestionOptions { FlushInterval = TimeSpan.FromMinutes(1), BatchSize = 100 },
            metrics,
            NullLogger.Instance);

        await ingestor.EnqueueAsync(CreateUpdate(instrument, "2026-07-12T00:00:00Z", 100m, isFinal: false), CancellationToken.None);
        await ingestor.EnqueueAsync(CreateUpdate(instrument, "2026-07-12T00:00:00Z", 105m, isFinal: false), CancellationToken.None);

        await ingestor.DisposeAsync();

        var stored = await store.GetRangeAsync(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-07-12T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-12T00:05:00Z"));
        stored.Should().ContainSingle();
        stored[0].Bar.BidClose.Should().Be(105m);
        metrics.Snapshot().PersistedUpdates.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_ShouldLetFinalUpdateSupersedeQueuedFormingUpdate()
    {
        var store = new InMemoryMarketDataStore();
        var healthStore = new InMemoryMarketDataHealthStore();
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        await using var ingestor = new MarketDataStreamBatchIngestor(
            store,
            healthStore,
            new FixedMarketDataClock(DateTimeOffset.Parse("2026-07-12T00:05:00Z")),
            new MarketDataCollectorOptions(),
            new MarketDataStreamIngestionOptions { FlushInterval = TimeSpan.FromMinutes(1), BatchSize = 100 },
            new MarketDataStreamPipelineMetrics(),
            NullLogger.Instance);

        await ingestor.EnqueueAsync(CreateUpdate(instrument, "2026-07-12T00:00:00Z", 100m, isFinal: false), CancellationToken.None);
        await ingestor.EnqueueAsync(CreateUpdate(instrument, "2026-07-12T00:00:00Z", 101m, isFinal: true), CancellationToken.None);

        await ingestor.DisposeAsync();

        var stored = await store.GetRangeAsync(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-07-12T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-12T00:05:00Z"));
        stored.Should().ContainSingle();
        stored[0].IsFinal.Should().BeTrue();
        stored[0].Bar.BidClose.Should().Be(101m);
        var health = await healthStore.GetAsync(instrument, PriceResolution.FiveMinutes);
        health!.LatestCompletedCandleUtc.Should().Be(DateTimeOffset.Parse("2026-07-12T00:00:00Z"));
    }

    [Fact]
    public async Task DisposeAsync_ShouldNotLetLaterFormingUpdateOverwriteFinalUpdate()
    {
        var store = new InMemoryMarketDataStore();
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        await using var ingestor = new MarketDataStreamBatchIngestor(
            store,
            new InMemoryMarketDataHealthStore(),
            new FixedMarketDataClock(DateTimeOffset.Parse("2026-07-12T00:05:00Z")),
            new MarketDataCollectorOptions(),
            new MarketDataStreamIngestionOptions { FlushInterval = TimeSpan.FromMinutes(1), BatchSize = 100 },
            new MarketDataStreamPipelineMetrics(),
            NullLogger.Instance);

        await ingestor.EnqueueAsync(CreateUpdate(instrument, "2026-07-12T00:00:00Z", 101m, isFinal: true), CancellationToken.None);
        await ingestor.EnqueueAsync(CreateUpdate(instrument, "2026-07-12T00:00:00Z", 99m, isFinal: false), CancellationToken.None);

        await ingestor.DisposeAsync();

        var stored = await store.GetRangeAsync(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-07-12T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-12T00:05:00Z"));
        stored.Should().ContainSingle();
        stored[0].IsFinal.Should().BeTrue();
        stored[0].Bar.BidClose.Should().Be(101m);
    }

    private static StreamPriceBarUpdate CreateUpdate(
        InstrumentId instrument,
        string timestampUtc,
        decimal close,
        bool isFinal)
        => new(
            instrument,
            PriceResolution.FiveMinutes,
            new PriceBar(
                DateTimeOffset.Parse(timestampUtc),
                99m,
                106m,
                98m,
                close,
                99.2m,
                106.2m,
                98.2m,
                close + 0.2m,
                10),
            isFinal,
            DateTimeOffset.Parse(timestampUtc).AddMinutes(5));
}
