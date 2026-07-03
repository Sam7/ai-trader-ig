using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.MarketData.Tests;

public sealed class MarketDataHistoricalBackfillServiceTests
{
    [Fact]
    public async Task BackfillAsync_ShouldCallIgRestAndPersistReturnedBars()
    {
        var store = new InMemoryMarketDataStore();
        var gateway = new FakeTradingGateway
        {
            PriceResponseFactory = request => new PriceSeries(
                request.Instrument,
                request.Resolution,
                [CreateBar("2026-06-29T00:00:00Z")]),
        };
        var service = new MarketDataHistoricalBackfillService(
            store,
            gateway,
            NullLogger<MarketDataHistoricalBackfillService>.Instance);
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");

        var count = await service.BackfillAsync(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z"));

        count.Should().Be(1);
        gateway.PriceRequests.Should().ContainSingle();
        var bars = await store.GetRangeAsync(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z"));
        bars.Should().ContainSingle();
        bars[0].Source.Should().Be(MarketDataSource.RestBackfill);
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

        public Func<GetPricesRequest, PriceSeries>? PriceResponseFactory { get; init; }

        public Task<ITradingSession> AuthenticateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<ITradingSession>(new FakeTradingSession("DEMO1234", "IG Demo", DateTimeOffset.UtcNow));

        public Task<PriceSeries> GetPricesAsync(GetPricesRequest request, CancellationToken cancellationToken = default)
        {
            PriceRequests.Add(request);
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
