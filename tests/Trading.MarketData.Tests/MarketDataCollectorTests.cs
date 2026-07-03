using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.MarketData.Tests;

public sealed class MarketDataCollectorTests
{
    [Fact]
    public async Task RunAsync_ShouldStartStreamingBeforeHistoricalRepair()
    {
        var log = new List<string>();
        var store = new InMemoryMarketDataStore();
        var stream = new FakeMarketDataStreamClient(log);
        var gateway = new FakeTradingGateway(log);
        var collector = CreateCollector(store, stream, gateway, nowUtc: "2026-06-29T00:17:00Z");

        await collector.RunAsync([new InstrumentId("CS.D.BITCOIN.CFD.IP")], TimeSpan.Zero);

        log.Should().ContainInOrder("stream:start", "history:CS.D.BITCOIN.CFD.IP:2026-06-28T18:15:00.0000000+00:00:2026-06-29T00:15:00.0000000+00:00");
    }

    [Fact]
    public async Task RunAsync_ShouldSubscribeAllMarketsOnOneStreamSession()
    {
        var store = new InMemoryMarketDataStore();
        var stream = new FakeMarketDataStreamClient();
        var collector = CreateCollector(store, stream, new FakeTradingGateway(), nowUtc: "2026-06-29T00:17:00Z");
        var bitcoin = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        var gold = new InstrumentId("CS.D.CFAGOLD.CFA.IP");

        await collector.RunAsync([bitcoin, gold], TimeSpan.Zero);

        stream.StartCalls.Should().Be(1);
        stream.Subscriptions.Should().Equal(
            new MarketDataStreamSubscription(bitcoin, PriceResolution.FiveMinutes),
            new MarketDataStreamSubscription(gold, PriceResolution.FiveMinutes));
    }

    [Fact]
    public async Task RunAsync_ShouldUpsertStreamUpdates()
    {
        var store = new InMemoryMarketDataStore();
        var stream = new FakeMarketDataStreamClient
        {
            OnStart = async handler =>
            {
                await handler(new StreamPriceBarUpdate(
                    new InstrumentId("CS.D.BITCOIN.CFD.IP"),
                    PriceResolution.FiveMinutes,
                    CreateBar("2026-06-29T00:15:00Z"),
                    IsFinal: false,
                    ObservedAtUtc: DateTimeOffset.Parse("2026-06-29T00:17:00Z")),
                    CancellationToken.None);
            },
        };
        var collector = CreateCollector(store, stream, new FakeTradingGateway(), nowUtc: "2026-06-29T00:17:00Z");

        await collector.RunAsync([new InstrumentId("CS.D.BITCOIN.CFD.IP")], TimeSpan.Zero);

        var stored = await store.GetRangeAsync(
            new InstrumentId("CS.D.BITCOIN.CFD.IP"),
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:15:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:20:00Z"));
        stored.Should().ContainSingle();
        stored[0].IsFinal.Should().BeFalse();
        stored[0].Source.Should().Be(MarketDataSource.Stream);
    }

    [Fact]
    public async Task RunAsync_ShouldFetchOnlyMissingCompletedBucketsAfterLatestFinal()
    {
        var store = new InMemoryMarketDataStore();
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        await store.UpsertAsync(
        [
            StoredPriceBar.FromPriceBar(
                instrument,
                PriceResolution.FiveMinutes,
                CreateBar("2026-06-29T00:00:00Z"),
                MarketDataSource.Stream),
        ]);
        var gateway = new FakeTradingGateway
        {
            PriceResponseFactory = request => new PriceSeries(
                request.Instrument,
                request.Resolution,
                [
                    CreateBar("2026-06-29T00:05:00Z"),
                    CreateBar("2026-06-29T00:10:00Z"),
                ]),
        };
        var collector = CreateCollector(store, new FakeMarketDataStreamClient(), gateway, nowUtc: "2026-06-29T00:17:00Z");

        await collector.RunAsync([instrument], TimeSpan.Zero);

        gateway.PriceRequests.Should().ContainSingle();
        gateway.PriceRequests[0].FromUtc.Should().Be(DateTimeOffset.Parse("2026-06-29T00:05:00Z"));
        gateway.PriceRequests[0].ToUtc.Should().Be(DateTimeOffset.Parse("2026-06-29T00:15:00Z"));
    }

    [Fact]
    public async Task RunAsync_WhenHistoryReturnsNoBars_ShouldRecordCoverageAndAvoidRepeatFetch()
    {
        var store = new InMemoryMarketDataStore();
        var healthStore = new InMemoryMarketDataHealthStore();
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        var gateway = new FakeTradingGateway();
        var collector = CreateCollector(store, new FakeMarketDataStreamClient(), gateway, healthStore, nowUtc: "2026-06-29T00:17:00Z");

        await collector.RunAsync([instrument], TimeSpan.Zero);
        await collector.RunAsync([instrument], TimeSpan.Zero);

        gateway.PriceRequests.Should().ContainSingle();
        var health = await healthStore.GetAsync(instrument, PriceResolution.FiveMinutes);
        health.Should().NotBeNull();
        health!.LastHistoricalRepairStatus.Should().Be(MarketDataCoverageStatus.NoBars);
    }

    [Fact]
    public async Task RunAsync_WhenHistoryAllowanceIsBlocked_ShouldMarkMarketDegradedAndKeepStreamHealthy()
    {
        var store = new InMemoryMarketDataStore();
        var healthStore = new InMemoryMarketDataHealthStore();
        var stream = new FakeMarketDataStreamClient();
        var gateway = new FakeTradingGateway
        {
            PriceException = new TradingGatewayException(TradingErrorCode.BrokerError, "IG API error: error.public-api.exceeded-account-historical-data-allowance."),
        };
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        var collector = CreateCollector(store, stream, gateway, healthStore, nowUtc: "2026-06-29T00:17:00Z");

        await collector.RunAsync([instrument], TimeSpan.Zero);

        stream.StartCalls.Should().Be(1);
        var health = await healthStore.GetAsync(instrument, PriceResolution.FiveMinutes);
        health.Should().NotBeNull();
        health!.ConnectionState.Should().Be(MarketDataConnectionState.Connected);
        health.RepairState.Should().Be(MarketDataRepairState.Degraded);
        health.LastHistoricalRepairStatus.Should().Be(MarketDataCoverageStatus.AllowanceBlocked);
    }

    [Fact]
    public async Task RunAsync_WithCloudMirrorEnabled_ShouldSkipHistoricalRepairAndKeepStreaming()
    {
        var store = new InMemoryMarketDataStore();
        var healthStore = new InMemoryMarketDataHealthStore();
        var stream = new FakeMarketDataStreamClient
        {
            OnStart = async handler =>
            {
                await handler(new StreamPriceBarUpdate(
                    new InstrumentId("CS.D.BITCOIN.CFD.IP"),
                    PriceResolution.FiveMinutes,
                    CreateBar("2026-06-29T00:15:00Z"),
                    IsFinal: true,
                    ObservedAtUtc: DateTimeOffset.Parse("2026-06-29T00:20:00Z")),
                    CancellationToken.None);
            },
        };
        var gateway = new FakeTradingGateway();
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        var collector = CreateCollector(
            store,
            stream,
            gateway,
            healthStore,
            nowUtc: "2026-06-29T00:20:00Z",
            marketDataOptions: new MarketDataOptions
            {
                CloudSnapshot = new MarketDataCloudSnapshotOptions
                {
                    Mirror = new MarketDataSnapshotMirrorOptions { Enabled = true },
                },
            });

        await collector.RunAsync([instrument], TimeSpan.Zero);

        gateway.PriceRequests.Should().BeEmpty();
        stream.StartCalls.Should().Be(1);
        var bars = await store.GetRangeAsync(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:15:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:20:00Z"));
        bars.Should().ContainSingle();
        var health = await healthStore.GetAsync(instrument, PriceResolution.FiveMinutes);
        health.Should().NotBeNull();
        health!.RepairState.Should().Be(MarketDataRepairState.Idle);
    }


    [Fact]
    public async Task RunAsync_WithIndefiniteDuration_ShouldWaitUntilCancelledAndDisposeStream()
    {
        var stream = new FakeMarketDataStreamClient();
        var collector = CreateCollector(
            new InMemoryMarketDataStore(),
            stream,
            new FakeTradingGateway(),
            nowUtc: "2026-06-29T00:17:00Z");
        using var cancellation = new CancellationTokenSource();

        var run = collector.RunAsync([new InstrumentId("CS.D.BITCOIN.CFD.IP")], null, cancellation.Token);
        await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        var action = async () => await run;
        await action.Should().ThrowAsync<OperationCanceledException>();
        stream.Session.DisposeCalls.Should().Be(1);
    }

    private static MarketDataCollector CreateCollector(
        IMarketDataStore store,
        FakeMarketDataStreamClient stream,
        FakeTradingGateway gateway,
        string nowUtc)
        => CreateCollector(store, stream, gateway, new InMemoryMarketDataHealthStore(), nowUtc);

    private static MarketDataCollector CreateCollector(
        IMarketDataStore store,
        FakeMarketDataStreamClient stream,
        FakeTradingGateway gateway,
        IMarketDataHealthStore healthStore,
        string nowUtc,
        MarketDataOptions? marketDataOptions = null)
        => new(
            stream,
            store,
            healthStore,
            (IMarketSessionEvidenceStore)store,
            gateway,
            new FixedMarketDataClock(DateTimeOffset.Parse(nowUtc)),
            Options.Create(marketDataOptions ?? new MarketDataOptions()),
            Options.Create(new MarketDataCollectorOptions()),
            NullLogger<MarketDataCollector>.Instance);

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

    private sealed class FakeMarketDataStreamClient : IMarketDataStreamClient
    {
        private readonly List<string>? _log;

        public FakeMarketDataStreamClient(List<string>? log = null)
        {
            _log = log;
        }

        public int StartCalls { get; private set; }

        public IReadOnlyList<MarketDataStreamSubscription> Subscriptions { get; private set; } = [];

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public NoOpMarketDataStreamSession Session { get; } = new();

        public Func<Func<StreamPriceBarUpdate, CancellationToken, Task>, Task>? OnStart { get; init; }

        public async Task<IMarketDataStreamSession> StartAsync(
            IReadOnlyList<MarketDataStreamSubscription> subscriptions,
            Func<StreamPriceBarUpdate, CancellationToken, Task> onUpdate,
            CancellationToken cancellationToken = default)
        {
            StartCalls++;
            Subscriptions = subscriptions;
            _log?.Add("stream:start");
            if (OnStart is not null)
            {
                await OnStart(onUpdate);
            }

            Started.TrySetResult();
            return Session;
        }
    }

    private sealed class NoOpMarketDataStreamSession : IMarketDataStreamSession
    {
        public int DisposeCalls { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeTradingGateway : ITradingGateway
    {
        private readonly List<string>? _log;

        public FakeTradingGateway(List<string>? log = null)
        {
            _log = log;
        }

        public List<GetPricesRequest> PriceRequests { get; } = [];

        public Func<GetPricesRequest, PriceSeries>? PriceResponseFactory { get; init; }

        public TradingGatewayException? PriceException { get; init; }

        public Task<ITradingSession> AuthenticateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<ITradingSession>(new FakeTradingSession("DEMO1234", "IG Demo", DateTimeOffset.UtcNow));

        public Task<PriceSeries> GetPricesAsync(GetPricesRequest request, CancellationToken cancellationToken = default)
        {
            PriceRequests.Add(request);
            _log?.Add($"history:{request.Instrument}:{request.FromUtc:O}:{request.ToUtc:O}");
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
