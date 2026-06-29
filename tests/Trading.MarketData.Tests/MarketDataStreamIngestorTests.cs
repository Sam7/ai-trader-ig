using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.MarketData.Tests;

public sealed class MarketDataStreamIngestorTests
{
    [Fact]
    public async Task IngestAsync_ShouldPersistCanonicalStreamBar()
    {
        var store = new InMemoryMarketDataStore();
        var ingestor = CreateIngestor(store);
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        var update = new StreamPriceBarUpdate(
            instrument,
            PriceResolution.FiveMinutes,
            CreateBar("2026-06-29T00:00:00Z"),
            IsFinal: true,
            ObservedAtUtc: DateTimeOffset.Parse("2026-06-29T00:05:01Z"));

        var result = await ingestor.IngestAsync(update);

        result.Status.Should().Be(MarketDataIngestStatus.Stored);
        var stored = await store.GetRangeAsync(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z"));
        stored.Should().ContainSingle();
        stored[0].Source.Should().Be(MarketDataSource.Stream);
        stored[0].IsFinal.Should().BeTrue();
    }

    [Fact]
    public async Task IngestAsync_ShouldReplacePartialBarWithFinalBarForSameBucket()
    {
        var store = new InMemoryMarketDataStore();
        var ingestor = CreateIngestor(store);
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");

        await ingestor.IngestAsync(new StreamPriceBarUpdate(
            instrument,
            PriceResolution.FiveMinutes,
            CreateBar("2026-06-29T00:00:00Z", bidClose: 100.5m),
            IsFinal: false,
            ObservedAtUtc: DateTimeOffset.Parse("2026-06-29T00:03:00Z")));
        await ingestor.IngestAsync(new StreamPriceBarUpdate(
            instrument,
            PriceResolution.FiveMinutes,
            CreateBar("2026-06-29T00:00:00Z", bidClose: 101.5m),
            IsFinal: true,
            ObservedAtUtc: DateTimeOffset.Parse("2026-06-29T00:05:01Z")));

        var stored = await store.GetRangeAsync(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z"));

        stored.Should().ContainSingle();
        stored[0].IsFinal.Should().BeTrue();
        stored[0].Bar.BidClose.Should().Be(101.5m);
        stored[0].FirstSeenUtc.Should().Be(DateTimeOffset.Parse("2026-06-29T00:03:00Z"));
        stored[0].LastSeenUtc.Should().Be(DateTimeOffset.Parse("2026-06-29T00:05:01Z"));
    }

    [Fact]
    public async Task IngestAsync_ShouldRejectNonCanonicalResolutionWithoutPersisting()
    {
        var store = new InMemoryMarketDataStore();
        var ingestor = CreateIngestor(store);
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");

        var result = await ingestor.IngestAsync(new StreamPriceBarUpdate(
            instrument,
            PriceResolution.TenMinutes,
            CreateBar("2026-06-29T00:00:00Z"),
            IsFinal: true,
            ObservedAtUtc: DateTimeOffset.Parse("2026-06-29T00:10:01Z")));

        result.Status.Should().Be(MarketDataIngestStatus.UnsupportedResolution);
        var stored = await store.GetRangeAsync(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:10:00Z"));
        stored.Should().BeEmpty();
    }

    private static MarketDataStreamIngestor CreateIngestor(IMarketDataStore store)
        => new(
            store,
            Options.Create(new MarketDataOptions()),
            NullLogger<MarketDataStreamIngestor>.Instance);

    private static PriceBar CreateBar(string timestampUtc, decimal bidClose = 100.5m)
        => new(
            DateTimeOffset.Parse(timestampUtc),
            100m,
            101m,
            99m,
            bidClose,
            100.2m,
            101.2m,
            99.2m,
            bidClose + 0.2m,
            10);
}
