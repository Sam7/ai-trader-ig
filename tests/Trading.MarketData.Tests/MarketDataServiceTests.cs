using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.MarketData.Tests;

public sealed class MarketDataServiceTests
{
    [Fact]
    public async Task GetBarsAsync_WithCompleteLocalCoverage_ShouldNotCallBroker()
    {
        var store = new InMemoryMarketDataStore();
        var gateway = new FakeTradingGateway();
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        await store.UpsertAsync(CreateStoredBars(instrument, "2026-06-29T00:00:00Z", 6, MarketDataSource.Stream));
        var service = CreateService(store, gateway);

        var result = await service.GetBarsAsync(new MarketDataRequest(
            instrument,
            PriceResolution.TenMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:30:00Z")));

        result.Status.Should().Be(MarketDataStatus.Completed);
        result.Source.Should().Be(MarketDataResultSource.LocalCache);
        result.Series.Bars.Should().HaveCount(3);
        gateway.PriceRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBarsAsync_WithMissingTail_ShouldFetchOnlyTheTailGapAndPersistIt()
    {
        var store = new InMemoryMarketDataStore();
        var gateway = new FakeTradingGateway();
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        await store.UpsertAsync(CreateStoredBars(instrument, "2026-06-29T00:00:00Z", 4, MarketDataSource.Stream));
        gateway.PriceResponseFactory = request => new PriceSeries(
            request.Instrument,
            request.Resolution,
            [CreateBar("2026-06-29T00:20:00Z"), CreateBar("2026-06-29T00:25:00Z")]);
        var service = CreateService(store, gateway);

        var result = await service.GetBarsAsync(new MarketDataRequest(
            instrument,
            PriceResolution.TenMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:30:00Z")));

        result.Status.Should().Be(MarketDataStatus.Completed);
        result.Source.Should().Be(MarketDataResultSource.Mixed);
        result.BackfilledBarCount.Should().Be(2);
        gateway.PriceRequests.Should().ContainSingle();
        gateway.PriceRequests[0].Resolution.Should().Be(PriceResolution.FiveMinutes);
        gateway.PriceRequests[0].FromUtc.Should().Be(DateTimeOffset.Parse("2026-06-29T00:20:00Z"));
        gateway.PriceRequests[0].ToUtc.Should().Be(DateTimeOffset.Parse("2026-06-29T00:30:00Z"));
    }

    [Fact]
    public async Task GetBarsAsync_WhenBackfillHitsAllowance_ShouldReturnBlockedResultWithLocalBars()
    {
        var store = new InMemoryMarketDataStore();
        var gateway = new FakeTradingGateway
        {
            PriceException = new TradingGatewayException(TradingErrorCode.BrokerError, "IG API error: error.public-api.exceeded-account-historical-data-allowance."),
        };
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        await store.UpsertAsync(CreateStoredBars(instrument, "2026-06-29T00:00:00Z", 2, MarketDataSource.Stream));
        var service = CreateService(store, gateway);

        var result = await service.GetBarsAsync(new MarketDataRequest(
            instrument,
            PriceResolution.TenMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:30:00Z")));

        result.Status.Should().Be(MarketDataStatus.BlockedBackfillAllowance);
        result.Series.Bars.Should().ContainSingle();
        result.Gaps.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetBarsAsync_WithOnlyUnfinishedLocalBar_ShouldBackfillTheBucket()
    {
        var store = new InMemoryMarketDataStore();
        var gateway = new FakeTradingGateway();
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        await store.UpsertAsync(
        [
            StoredPriceBar.FromPriceBar(
                instrument,
                PriceResolution.FiveMinutes,
                CreateBar("2026-06-29T00:00:00Z"),
                MarketDataSource.Stream,
                isFinal: false),
        ]);
        gateway.PriceResponseFactory = request => new PriceSeries(
            request.Instrument,
            request.Resolution,
            [CreateBar("2026-06-29T00:00:00Z")]);
        var service = CreateService(store, gateway);

        var result = await service.GetBarsAsync(new MarketDataRequest(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z")));

        result.Status.Should().Be(MarketDataStatus.Completed);
        result.Source.Should().Be(MarketDataResultSource.RestBackfill);
        gateway.PriceRequests.Should().ContainSingle();
        result.Series.Bars.Should().ContainSingle();
    }

    [Fact]
    public async Task GetBarsAsync_WithUnsupportedResolution_ShouldNotCallBroker()
    {
        var gateway = new FakeTradingGateway();
        var service = CreateService(new InMemoryMarketDataStore(), gateway);

        var result = await service.GetBarsAsync(new MarketDataRequest(
            new InstrumentId("CS.D.BITCOIN.CFD.IP"),
            PriceResolution.Minute,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:30:00Z")));

        result.Status.Should().Be(MarketDataStatus.UnsupportedResolution);
        gateway.PriceRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBarsAsync_WithBackfillDisabledAndNoLocalBars_ShouldReturnPartialWithNoSource()
    {
        var gateway = new FakeTradingGateway();
        var service = CreateService(
            new InMemoryMarketDataStore(),
            gateway,
            new MarketDataOptions { BackfillEnabled = false });

        var result = await service.GetBarsAsync(new MarketDataRequest(
            new InstrumentId("CS.D.BITCOIN.CFD.IP"),
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z")));

        result.Status.Should().Be(MarketDataStatus.Partial);
        result.Source.Should().Be(MarketDataResultSource.None);
        result.Series.Bars.Should().BeEmpty();
        gateway.PriceRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBarsAsync_WithCloudMirrorEnabled_ShouldNotAutomaticallyBackfillFromBroker()
    {
        var gateway = new FakeTradingGateway();
        var service = CreateService(
            new InMemoryMarketDataStore(),
            gateway,
            new MarketDataOptions
            {
                BackfillEnabled = true,
                CloudSnapshot = new MarketDataCloudSnapshotOptions
                {
                    Mirror = new MarketDataSnapshotMirrorOptions { Enabled = true },
                },
            });

        var result = await service.GetBarsAsync(new MarketDataRequest(
            new InstrumentId("CS.D.BITCOIN.CFD.IP"),
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z")));

        result.Status.Should().Be(MarketDataStatus.Partial);
        result.Source.Should().Be(MarketDataResultSource.None);
        gateway.PriceRequests.Should().BeEmpty();
    }

    private static MarketDataService CreateService(IMarketDataStore store, FakeTradingGateway gateway)
        => CreateService(store, gateway, new MarketDataOptions());

    private static MarketDataService CreateService(
        IMarketDataStore store,
        FakeTradingGateway gateway,
        MarketDataOptions options)
        => new(
            store,
            gateway,
            Options.Create(options),
            NullLogger<MarketDataService>.Instance);

    private static IReadOnlyList<StoredPriceBar> CreateStoredBars(
        InstrumentId instrument,
        string startUtc,
        int count,
        MarketDataSource source)
    {
        var start = DateTimeOffset.Parse(startUtc);
        return Enumerable.Range(0, count)
            .Select(index => StoredPriceBar.FromPriceBar(
                instrument,
                PriceResolution.FiveMinutes,
                CreateBar(start.AddMinutes(index * 5).ToString("O")),
                source))
            .ToArray();
    }

    private static PriceBar CreateBar(string timestampUtc)
        => new(
            DateTimeOffset.Parse(timestampUtc),
            100m,
            101m,
            99m,
            100.5m,
            100.2m,
            101.2m,
            99.2m,
            100.7m,
            10);

    private sealed class FakeTradingGateway : ITradingGateway
    {
        public List<GetPricesRequest> PriceRequests { get; } = [];

        public Func<GetPricesRequest, PriceSeries>? PriceResponseFactory { get; set; }

        public TradingGatewayException? PriceException { get; init; }

        public Task<ITradingSession> AuthenticateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<ITradingSession>(new FakeTradingSession("DEMO1234", "IG Demo", DateTimeOffset.UtcNow));

        public Task<PriceSeries> GetPricesAsync(GetPricesRequest request, CancellationToken cancellationToken = default)
        {
            PriceRequests.Add(request);
            if (PriceException is not null)
            {
                throw PriceException;
            }

            return Task.FromResult(PriceResponseFactory?.Invoke(request) ?? new PriceSeries(request.Instrument, request.Resolution, []));
        }

        public Task<PlaceOrderResult> PlaceMarketOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkingOrderResult> PlaceWorkingOrderAsync(CreateWorkingOrderRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ClosePositionResult> ClosePositionAsync(ClosePositionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UpdatePositionResult> UpdatePositionAsync(UpdatePositionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkingOrderResult> UpdateWorkingOrderAsync(UpdateWorkingOrderRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkingOrderResult> CancelWorkingOrderAsync(string dealId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PositionSummary>> GetOpenPositionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkingOrderSummary>> GetWorkingOrdersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MarketSearchResult>> SearchMarketsAsync(string searchTerm, int maxResults = 20, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MarketDetails> GetMarketDetailsAsync(InstrumentId instrument, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MarketNavigationPage> BrowseMarketsAsync(string? nodeId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrderSummary>> GetOrdersAsync(OrderQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OrderSummary?> GetOrderStatusAsync(string dealReference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record FakeTradingSession(string AccountId, string BrokerName, DateTimeOffset AuthenticatedAtUtc) : ITradingSession;
}
