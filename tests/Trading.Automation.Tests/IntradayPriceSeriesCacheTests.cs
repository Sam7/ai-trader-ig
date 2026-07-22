using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.Abstractions;
using Trading.Automation.Execution;
using Trading.MarketData;

public sealed class IntradayPriceSeriesCacheTests
{
    [Fact]
    public async Task GetSeriesAsync_WithNoCachedBars_ShouldReturnAnEmptyLocalResult()
    {
        var cache = CreateCache();

        var result = await cache.GetSeriesAsync(
            new InstrumentId("CS.D.BITCOIN.CFD.IP"),
            DateTimeOffset.Parse("2026-06-28T04:30:00Z"),
            chartLookbackHours: 1,
            PriceResolution.TenMinutes);

        result.RefreshMode.Should().Be(PriceSeriesRefreshMode.LocalCache);
        result.FetchedBarCount.Should().Be(0);
        result.Series.Bars.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSeriesAsync_ShouldReadCoveredLookbackFromLocalCache()
    {
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        var store = new InMemoryMarketDataStore();
        await store.UpsertAsync(CreateStoredFiveMinuteBars(
            instrument,
            DateTimeOffset.Parse("2026-06-28T03:30:00Z"),
            DateTimeOffset.Parse("2026-06-28T04:30:00Z")));
        var cache = CreateCache(store);

        var result = await cache.GetSeriesAsync(
            instrument,
            DateTimeOffset.Parse("2026-06-28T04:30:00Z"),
            chartLookbackHours: 1,
            PriceResolution.TenMinutes);

        result.RefreshMode.Should().Be(PriceSeriesRefreshMode.LocalCache);
        result.FetchedBarCount.Should().Be(0);
        result.Series.Bars.Should().HaveCount(6);
    }

    [Fact]
    public async Task GetSeriesAsync_WithMissingCanonicalGap_ShouldReturnAvailableLocalBarsOnly()
    {
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        var store = new InMemoryMarketDataStore();
        await store.UpsertAsync(CreateStoredFiveMinuteBars(
            instrument,
            DateTimeOffset.Parse("2026-06-28T04:00:00Z"),
            DateTimeOffset.Parse("2026-06-28T04:30:00Z")));
        var cache = CreateCache(store);

        var result = await cache.GetSeriesAsync(
            instrument,
            DateTimeOffset.Parse("2026-06-28T04:30:00Z"),
            chartLookbackHours: 1,
            PriceResolution.TenMinutes);

        result.RefreshMode.Should().Be(PriceSeriesRefreshMode.LocalCache);
        result.FetchedBarCount.Should().Be(0);
        result.Series.Bars.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetSeriesAsync_ShouldRemainLocalAcrossSuccessiveRequests()
    {
        var cache = CreateCache();
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");

        var first = await cache.GetSeriesAsync(
            instrument,
            DateTimeOffset.Parse("2026-06-28T04:30:00Z"),
            chartLookbackHours: 1,
            PriceResolution.TenMinutes);
        var second = await cache.GetSeriesAsync(
            instrument,
            DateTimeOffset.Parse("2026-06-28T04:40:00Z"),
            chartLookbackHours: 1,
            PriceResolution.TenMinutes);

        first.RefreshMode.Should().Be(PriceSeriesRefreshMode.LocalCache);
        second.RefreshMode.Should().Be(PriceSeriesRefreshMode.LocalCache);
        first.Series.Bars.Should().BeEmpty();
        second.Series.Bars.Should().BeEmpty();
    }

    private static IntradayPriceSeriesCache CreateCache(IMarketDataStore? store = null)
        => new(
            new MarketDataService(store ?? new InMemoryMarketDataStore(), Options.Create(new MarketDataOptions())),
            NullLogger<IntradayPriceSeriesCache>.Instance);

    private static IReadOnlyList<StoredPriceBar> CreateStoredFiveMinuteBars(
        InstrumentId instrument,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
        => Enumerable.Range(0, (int)((toUtc - fromUtc).TotalMinutes / 5))
            .Select(index => StoredPriceBar.FromPriceBar(
                instrument,
                PriceResolution.FiveMinutes,
                CreateBar(fromUtc.AddMinutes(index * 5)),
                MarketDataSource.Stream))
            .ToArray();

    private static PriceBar CreateBar(DateTimeOffset timestampUtc)
        => new(
            timestampUtc,
            100m,
            101m,
            99m,
            100.5m,
            100.2m,
            101.2m,
            99.2m,
            100.7m,
            10);
}
