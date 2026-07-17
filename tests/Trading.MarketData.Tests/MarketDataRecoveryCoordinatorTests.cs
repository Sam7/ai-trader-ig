using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.MarketData.Tests;

public sealed class MarketDataRecoveryCoordinatorTests
{
    [Fact]
    public async Task RecoverOnceAsync_UsesHighestPriorityAndNewestChunk()
    {
        var now = DateTimeOffset.Parse("2026-07-13T00:00:00Z");
        var store = new InMemoryMarketDataStore();
        var gateway = new RecoveryGateway(request => new PriceSeries(request.Instrument, request.Resolution, Bars(request.FromUtc!.Value, request.ToUtc!.Value), new HistoricalPriceAllowance(9_750, TimeSpan.FromDays(7))));
        var service = Create(store, gateway, now);
        var gold = new InstrumentId("GOLD");
        var wti = new InstrumentId("WTI");

        await service.RecoverOnceAsync([new MarketDataRecoveryTarget(wti, 2), new MarketDataRecoveryTarget(gold, 1)], PriceResolution.FiveMinutes);

        gateway.Requests.Should().ContainSingle();
        gateway.Requests[0].Instrument.Should().Be(gold);
        gateway.Requests[0].FromUtc.Should().Be(now.AddMinutes(-250 * 5));
        gateway.Requests[0].ToUtc.Should().Be(now);
    }

    [Fact]
    public async Task RecoverOnceAsync_NoBarsCompletesRangeAndDoesNotRequestItAgain()
    {
        var now = DateTimeOffset.Parse("2026-07-13T00:00:00Z");
        var store = new InMemoryMarketDataStore();
        var gateway = new RecoveryGateway(request => new PriceSeries(request.Instrument, request.Resolution, []));
        var service = Create(store, gateway, now);
        var target = new MarketDataRecoveryTarget(new InstrumentId("GOLD"), 1);

        await service.RecoverOnceAsync([target], PriceResolution.FiveMinutes);
        gateway.Requests.Should().HaveCount(1);
        (await store.GetCoverageAsync(target.Instrument, PriceResolution.FiveMinutes, now.AddDays(-14), now)).Should().ContainSingle(x => x.Status == MarketDataCoverageStatus.NoBars);
    }

    [Fact]
    public async Task RecoverOnceAsync_WhenAllowanceFails_PersistsAnEstimatedOneHourExpiry()
    {
        var now = DateTimeOffset.Parse("2026-07-13T00:00:00Z");
        var store = new InMemoryMarketDataStore();
        var gateway = new RecoveryGateway(_ => throw new TradingGatewayException(
            TradingErrorCode.BrokerError,
            "IG API error: error.public-api.exceeded-account-historical-data-allowance."));
        var service = Create(store, gateway, now);
        var target = new MarketDataRecoveryTarget(new InstrumentId("GOLD"), 1);

        var result = await service.RecoverOnceAsync([target], PriceResolution.FiveMinutes);

        result.BlockedRanges.Should().ContainSingle();
        result.RemainingAllowance.Should().Be(0);
        result.AllowanceExpiresAtUtc.Should().Be(now.AddHours(1));
        (await store.GetRecoveryStatesAsync()).Should().ContainSingle(state =>
            state.AllowanceExpiresAtUtc == now.AddHours(1)
            && state.LastFailure!.Contains("allowance", StringComparison.OrdinalIgnoreCase));
    }

    private static MarketDataRecoveryCoordinator Create(InMemoryMarketDataStore store, RecoveryGateway gateway, DateTimeOffset now)
        => new(store, store, gateway, new FixedMarketDataClock(now), new MarketDataRecoveryOptions(), NullLogger<MarketDataRecoveryCoordinator>.Instance);

    private static IReadOnlyList<PriceBar> Bars(DateTimeOffset from, DateTimeOffset to)
        => Enumerable.Range(0, (int)((to - from).TotalMinutes / 5)).Select(i => new PriceBar(from.AddMinutes(i * 5), 1, 1, 1, 1, 1, 1, 1, 1, null)).ToArray();

    private sealed class RecoveryGateway(Func<GetPricesRequest, PriceSeries> prices) : ITradingGateway
    {
        public List<GetPricesRequest> Requests { get; } = [];
        public Task<ITradingSession> AuthenticateAsync(CancellationToken cancellationToken = default) => Task.FromResult<ITradingSession>(new Session());
        public Task<PriceSeries> GetPricesAsync(GetPricesRequest request, CancellationToken cancellationToken = default) { Requests.Add(request); return Task.FromResult(prices(request)); }
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
        private sealed record Session() : ITradingSession { public string AccountId => "demo"; public string BrokerName => "fake"; public DateTimeOffset AuthenticatedAtUtc => DateTimeOffset.UtcNow; }
    }
}
