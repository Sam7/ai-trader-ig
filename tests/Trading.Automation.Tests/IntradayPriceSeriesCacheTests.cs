using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.Abstractions;
using Trading.Automation.Execution;
using Trading.MarketData;

public sealed class IntradayPriceSeriesCacheTests
{
    [Fact]
    public async Task GetSeriesAsync_ShouldAuthenticateBeforeRetrievingPrices()
    {
        var gateway = new FakeTradingGateway();
        var cache = CreateCache(gateway);

        await cache.GetSeriesAsync(
            new InstrumentId("CS.D.BITCOIN.CFD.IP"),
            DateTimeOffset.Parse("2026-06-28T04:30:00Z"),
            chartLookbackHours: 1,
            PriceResolution.TenMinutes);

        gateway.Calls.Should().StartWith(["Authenticate", "GetPrices"]);
    }

    [Fact]
    public async Task GetSeriesAsync_ShouldReuseAuthenticatedSessionAcrossRefreshes()
    {
        var gateway = new FakeTradingGateway();
        var cache = CreateCache(gateway);
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");

        await cache.GetSeriesAsync(
            instrument,
            DateTimeOffset.Parse("2026-06-28T04:30:00Z"),
            chartLookbackHours: 1,
            PriceResolution.TenMinutes);
        await cache.GetSeriesAsync(
            instrument,
            DateTimeOffset.Parse("2026-06-28T04:40:00Z"),
            chartLookbackHours: 1,
            PriceResolution.TenMinutes);

        gateway.Calls.Where(call => call == "Authenticate").Should().ContainSingle();
        gateway.Calls.Where(call => call == "GetPrices").Should().HaveCount(2);
    }

    [Fact]
    public async Task GetSeriesAsync_ShouldReadFromLocalCacheWhenLookbackIsAlreadyCovered()
    {
        var gateway = new FakeTradingGateway();
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        var store = new InMemoryMarketDataStore();
        await store.UpsertAsync(CreateStoredFiveMinuteBars(
            instrument,
            DateTimeOffset.Parse("2026-06-28T03:30:00Z"),
            DateTimeOffset.Parse("2026-06-28T04:30:00Z")));
        var cache = CreateCache(gateway, store);

        var result = await cache.GetSeriesAsync(
            instrument,
            DateTimeOffset.Parse("2026-06-28T04:30:00Z"),
            chartLookbackHours: 1,
            PriceResolution.TenMinutes);

        result.RefreshMode.Should().Be(PriceSeriesRefreshMode.LocalCache);
        result.FetchedBarCount.Should().Be(0);
        result.Series.Bars.Should().HaveCount(6);
        gateway.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSeriesAsync_ShouldBackfillOnlyMissingCanonicalGapWhenLocalCacheHasLookback()
    {
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        var gateway = new FakeTradingGateway();
        var cache = CreateCache(gateway);

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

        first.RefreshMode.Should().Be(PriceSeriesRefreshMode.Bootstrap);
        second.RefreshMode.Should().Be(PriceSeriesRefreshMode.Incremental);
        gateway.PriceRequests.Select(request => (
                request.Resolution,
                request.FromUtc,
                request.ToUtc))
            .Should()
            .Equal(
                (
                    PriceResolution.FiveMinutes,
                    (DateTimeOffset?)DateTimeOffset.Parse("2026-06-28T03:30:00Z"),
                    (DateTimeOffset?)DateTimeOffset.Parse("2026-06-28T04:30:00Z")),
                (
                    PriceResolution.FiveMinutes,
                    (DateTimeOffset?)DateTimeOffset.Parse("2026-06-28T04:30:00Z"),
                    (DateTimeOffset?)DateTimeOffset.Parse("2026-06-28T04:40:00Z")));
    }

    [Fact]
    public async Task GetSeriesAsync_ShouldReauthenticateOnceWhenSessionIsRejected()
    {
        var gateway = new FakeTradingGateway
        {
            FailFirstPriceRequest = true,
        };
        var cache = CreateCache(gateway);

        var result = await cache.GetSeriesAsync(
            new InstrumentId("CS.D.BITCOIN.CFD.IP"),
            DateTimeOffset.Parse("2026-06-28T04:30:00Z"),
            chartLookbackHours: 1,
            PriceResolution.TenMinutes);

        result.Series.Bars.Should().NotBeEmpty();
        gateway.Calls.Should().Equal("Authenticate", "GetPrices", "Authenticate", "GetPrices");
    }

    private static IntradayPriceSeriesCache CreateCache(
        FakeTradingGateway gateway,
        IMarketDataStore? store = null)
    {
        var marketDataService = new MarketDataService(
            store ?? new InMemoryMarketDataStore(),
            gateway,
            Options.Create(new MarketDataOptions()),
            NullLogger<MarketDataService>.Instance);

        return new IntradayPriceSeriesCache(
            marketDataService,
            NullLogger<IntradayPriceSeriesCache>.Instance);
    }

    private sealed class FakeTradingGateway : ITradingGateway
    {
        private int _priceRequests;

        public List<string> Calls { get; } = [];

        public List<GetPricesRequest> PriceRequests { get; } = [];

        public bool FailFirstPriceRequest { get; init; }

        public Task<ITradingSession> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("Authenticate");
            return Task.FromResult<ITradingSession>(
                new FakeTradingSession("DEMO1234", "IG Demo", DateTimeOffset.Parse("2026-06-28T04:00:00Z")));
        }

        public Task<PriceSeries> GetPricesAsync(GetPricesRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("GetPrices");
            PriceRequests.Add(request);
            _priceRequests++;
            if (FailFirstPriceRequest && _priceRequests == 1)
            {
                throw new TradingGatewayException(TradingErrorCode.SessionExpired, "missing token");
            }

            var fromUtc = request.FromUtc ?? throw new InvalidOperationException("Expected range-based price request.");
            var toUtc = request.ToUtc ?? throw new InvalidOperationException("Expected range-based price request.");

            return Task.FromResult(new PriceSeries(
                request.Instrument,
                request.Resolution,
                CreateBars(fromUtc, toUtc, TimeSpan.FromMinutes(5))));
        }

        public Task<PlaceOrderResult> PlaceMarketOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<WorkingOrderResult> PlaceWorkingOrderAsync(CreateWorkingOrderRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ClosePositionResult> ClosePositionAsync(ClosePositionRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<UpdatePositionResult> UpdatePositionAsync(UpdatePositionRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<WorkingOrderResult> UpdateWorkingOrderAsync(UpdateWorkingOrderRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<WorkingOrderResult> CancelWorkingOrderAsync(string dealId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PositionSummary>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<WorkingOrderSummary>> GetWorkingOrdersAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<MarketSearchResult>> SearchMarketsAsync(
            string searchTerm,
            int maxResults = 20,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MarketDetails> GetMarketDetailsAsync(InstrumentId instrument, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MarketNavigationPage> BrowseMarketsAsync(string? nodeId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<OrderSummary>> GetOrdersAsync(OrderQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OrderSummary?> GetOrderStatusAsync(string dealReference, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed record FakeTradingSession(
        string AccountId,
        string BrokerName,
        DateTimeOffset AuthenticatedAtUtc) : ITradingSession;

    private static IReadOnlyList<StoredPriceBar> CreateStoredFiveMinuteBars(
        InstrumentId instrument,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
        => CreateBars(fromUtc, toUtc, TimeSpan.FromMinutes(5))
            .Select(bar => StoredPriceBar.FromPriceBar(instrument, PriceResolution.FiveMinutes, bar, MarketDataSource.Stream))
            .ToArray();

    private static IReadOnlyList<PriceBar> CreateBars(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        TimeSpan interval)
    {
        var bars = new List<PriceBar>();
        var timestamp = fromUtc;

        while (timestamp < toUtc)
        {
            bars.Add(CreateBar(timestamp));
            timestamp = timestamp.Add(interval);
        }

        return bars;
    }

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
