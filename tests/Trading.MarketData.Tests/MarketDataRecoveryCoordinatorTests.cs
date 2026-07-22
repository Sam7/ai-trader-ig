using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.MarketData.Tests;

public sealed class MarketDataRecoveryCoordinatorTests
{
    [Fact]
    public void RecoveryOptions_ShouldRejectAnUnsafeRequestRate()
    {
        var options = new MarketDataRecoveryOptions { MaximumRequestsPerMinute = 0 };

        var action = options.Validate;

        action.Should().Throw<InvalidOperationException>().WithMessage("*request and allowance settings*");
    }

    [Fact]
    public async Task ProcessNextAsync_UsesRecentWorkBeforeHistoricalWork()
    {
        var now = DateTimeOffset.Parse("2026-07-13T00:00:00Z");
        var store = new InMemoryMarketDataStore();
        var gateway = new RecoveryGateway(request => new PriceSeries(request.Instrument, request.Resolution, Bars(request.FromUtc!.Value, request.ToUtc!.Value), new HistoricalPriceAllowance(9_750, TimeSpan.FromDays(7))));
        var service = Create(store, gateway, now);
        var gold = new InstrumentId("GOLD");
        var wti = new InstrumentId("WTI");
        await store.UpsertRecoveryWorkItemAsync(Work(wti, MarketDataRecoveryReason.HistoricalAudit, 1, now.AddHours(-2), now));
        await store.UpsertRecoveryWorkItemAsync(Work(gold, MarketDataRecoveryReason.RecentTail, 2, now.AddMinutes(-10), now));

        (await service.ProcessNextAsync(MarketDataRecoveryMode.RecentAndHistorical)).Should().BeTrue();

        gateway.Requests.Should().ContainSingle();
        gateway.Requests[0].Instrument.Should().Be(gold);
    }

    [Fact]
    public async Task ProcessNextAsync_UsesDeploymentContinuityBeforeOtherAutomaticWork()
    {
        var now = DateTimeOffset.Parse("2026-07-13T00:00:00Z");
        var store = new InMemoryMarketDataStore();
        var gateway = new RecoveryGateway(request => new PriceSeries(
            request.Instrument,
            request.Resolution,
            Bars(request.FromUtc!.Value, request.ToUtc!.Value)));
        var service = Create(store, gateway, now);
        var deployment = new InstrumentId("GOLD");
        await store.UpsertRecoveryWorkItemAsync(Work(new InstrumentId("WTI"), MarketDataRecoveryReason.RecentTail, 1, now.AddMinutes(-10), now));
        await store.UpsertRecoveryWorkItemAsync(Work(deployment, MarketDataRecoveryReason.DeploymentContinuity, -1_000_000, now.AddMinutes(-10), now));

        (await service.ProcessNextAsync(MarketDataRecoveryMode.RecentOnly)).Should().BeTrue();

        gateway.Requests.Should().ContainSingle();
        gateway.Requests[0].Instrument.Should().Be(deployment);
    }

    [Fact]
    public async Task ProcessNextAsync_NoBarsCompletesWorkAndRecordsCoverage()
    {
        var now = DateTimeOffset.Parse("2026-07-13T00:00:00Z");
        var store = new InMemoryMarketDataStore();
        var metrics = new MarketDataRuntimeActivityMetrics();
        var service = Create(store, new RecoveryGateway(_ => new PriceSeries(new InstrumentId("GOLD"), PriceResolution.FiveMinutes, [])), now, metrics);
        var target = new InstrumentId("GOLD");
        await store.UpsertRecoveryWorkItemAsync(Work(target, MarketDataRecoveryReason.RecentTail, 1, now.AddMinutes(-10), now));

        (await service.ProcessNextAsync(MarketDataRecoveryMode.RecentOnly)).Should().BeTrue();

        (await store.GetRecoveryWorkItemsAsync()).Should().ContainSingle().Which.Status.Should().Be(MarketDataRecoveryWorkStatus.Completed);
        (await store.GetCoverageAsync(target, PriceResolution.FiveMinutes, now.AddMinutes(-10), now))
            .Should().ContainSingle(x => x.Status == MarketDataCoverageStatus.NoBars);
        var activity = metrics.Snapshot();
        activity.RecoveryStartedCount.Should().Be(1);
        activity.RecoveryCompletedCount.Should().Be(1);
        activity.RecoveryFailedCount.Should().Be(0);
        activity.ActiveRecoveryCount.Should().Be(0);
    }

    [Fact]
    public async Task ProcessNextAsync_WhenAllowanceFails_DefersWorkAndPersistsEstimatedBudget()
    {
        var now = DateTimeOffset.Parse("2026-07-13T00:00:00Z");
        var store = new InMemoryMarketDataStore();
        var gateway = new RecoveryGateway(_ => throw new TradingGatewayException(TradingErrorCode.BrokerError, "IG API error: error.public-api.exceeded-account-historical-data-allowance."));
        var service = Create(store, gateway, now);
        await store.UpsertRecoveryWorkItemAsync(Work(new InstrumentId("GOLD"), MarketDataRecoveryReason.RecentTail, 1, now.AddMinutes(-10), now));

        (await service.ProcessNextAsync(MarketDataRecoveryMode.RecentOnly)).Should().BeFalse();

        var budget = await store.GetHistoricalAllowanceBudgetAsync();
        budget.Should().Be(new HistoricalAllowanceBudget(0, now.AddHours(1), now, now.AddHours(1), ResetEstimated: true));
        (await store.GetRecoveryWorkItemsAsync()).Should().ContainSingle().Which.NextAttemptUtc.Should().Be(now.AddHours(1));
    }

    [Fact]
    public async Task ProcessNextAsync_DoesNotSpendHistoricalReserve()
    {
        var now = DateTimeOffset.Parse("2026-07-13T00:00:00Z");
        var store = new InMemoryMarketDataStore();
        var gateway = new RecoveryGateway(request => new PriceSeries(request.Instrument, request.Resolution, Bars(request.FromUtc!.Value, request.ToUtc!.Value)));
        var service = Create(store, gateway, now);
        await store.UpsertHistoricalAllowanceBudgetAsync(new HistoricalAllowanceBudget(2_000, now.AddDays(7), now));
        await store.UpsertRecoveryWorkItemAsync(Work(new InstrumentId("GOLD"), MarketDataRecoveryReason.HistoricalAudit, 1, now.AddHours(-2), now));

        (await service.ProcessNextAsync(MarketDataRecoveryMode.RecentAndHistorical)).Should().BeFalse();

        gateway.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessNextAsync_ObserveModeDoesNotAuthenticateOrCallIg()
    {
        var now = DateTimeOffset.Parse("2026-07-13T00:00:00Z");
        var store = new InMemoryMarketDataStore();
        var gateway = new RecoveryGateway(request => new PriceSeries(request.Instrument, request.Resolution, []));
        var service = Create(store, gateway, now);
        await store.UpsertRecoveryWorkItemAsync(Work(new InstrumentId("GOLD"), MarketDataRecoveryReason.RecentTail, 1, now.AddMinutes(-10), now));

        (await service.ProcessNextAsync(MarketDataRecoveryMode.Observe)).Should().BeFalse();

        gateway.Requests.Should().BeEmpty();
        (await store.GetRecoveryWorkItemsAsync()).Should().ContainSingle().Which.Status.Should().Be(MarketDataRecoveryWorkStatus.Pending);
    }

    private static MarketDataRecoveryWorkItem Work(InstrumentId instrument, MarketDataRecoveryReason reason, int priority, DateTimeOffset fromUtc, DateTimeOffset toUtc)
        => new(instrument, PriceResolution.FiveMinutes, reason, priority, fromUtc, toUtc, fromUtc, MarketDataRecoveryWorkStatus.Pending, fromUtc, 0, 0);

    private static MarketDataRecoveryCoordinator Create(
        InMemoryMarketDataStore store,
        RecoveryGateway gateway,
        DateTimeOffset now,
        MarketDataRuntimeActivityMetrics? metrics = null)
        => new(store, store, gateway, new FixedMarketDataClock(now), new MarketDataRecoveryOptions(), metrics ?? new MarketDataRuntimeActivityMetrics(), NullLogger<MarketDataRecoveryCoordinator>.Instance);

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
