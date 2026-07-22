using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.MarketData.Tests;

public sealed class MarketDataDeploymentContinuityServiceTests
{
    [Fact]
    public async Task CreateCheckpointAsync_ShouldBlockDeploymentWhenSnapshotIsNotPublished()
    {
        var now = DateTimeOffset.Parse("2026-07-23T00:15:00Z");
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        var store = new InMemoryMarketDataStore();
        await store.UpsertAsync([Stored(instrument, "2026-07-23T00:00:00Z")]);
        await using var workspace = new ContinuityWorkspace();
        var service = Create(store, new ContinuityGateway(_ => throw new InvalidOperationException()), now, workspace.Options);

        var action = () => service.CreateCheckpointAsync("test-deployment", [instrument], PriceResolution.FiveMinutes);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Pre-deployment market-data snapshot failed: Snapshot publisher is disabled.");
        (await service.GetActiveCheckpointAsync()).Should().BeNull();
    }

    [Fact]
    public async Task ReconcileAsync_ShouldRepairOnlyTheMissingDeploymentRange()
    {
        var now = DateTimeOffset.Parse("2026-07-23T00:15:00Z");
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        var store = new InMemoryMarketDataStore();
        await store.UpsertAsync([Stored(instrument, "2026-07-23T00:00:00Z")]);
        var gateway = new ContinuityGateway(request => new PriceSeries(
            request.Instrument,
            request.Resolution,
            Bars(request.FromUtc!.Value, request.ToUtc!.Value)));
        await using var workspace = new ContinuityWorkspace();
        var service = Create(store, gateway, now, workspace.Options);
        var checkpoint = Checkpoint(instrument, now, "2026-07-23T00:00:00Z");

        var report = await service.ReconcileAsync(checkpoint);

        report.Status.Should().Be(MarketDataDeploymentContinuityStatus.Succeeded);
        report.Ranges.Should().ContainSingle(range => range.Succeeded && !range.ConfirmedClosedMarket);
        gateway.PriceRequests.Should().ContainSingle();
        gateway.PriceRequests[0].FromUtc.Should().Be(DateTimeOffset.Parse("2026-07-23T00:05:00Z"));
        gateway.PriceRequests[0].ToUtc.Should().Be(DateTimeOffset.Parse("2026-07-23T00:15:00Z"));
        (await store.FindMissingCompletedRangesAsync(instrument, PriceResolution.FiveMinutes, DateTimeOffset.Parse("2026-07-23T00:05:00Z"), now))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task ReconcileAsync_ShouldAcceptNoBarsOnlyWhenIgConfirmsClosedMarket()
    {
        var now = DateTimeOffset.Parse("2026-07-23T00:15:00Z");
        var instrument = new InstrumentId("CS.D.CFAGOLD.CFA.IP");
        var store = new InMemoryMarketDataStore();
        await store.UpsertAsync([Stored(instrument, "2026-07-23T00:00:00Z")]);
        var gateway = new ContinuityGateway(request => new PriceSeries(request.Instrument, request.Resolution, []))
        {
            MarketStatus = MarketStatus.Closed,
        };
        await using var workspace = new ContinuityWorkspace();
        var service = Create(store, gateway, now, workspace.Options);

        var report = await service.ReconcileAsync(Checkpoint(instrument, now, "2026-07-23T00:00:00Z"));

        report.Status.Should().Be(MarketDataDeploymentContinuityStatus.Succeeded);
        report.Ranges.Should().ContainSingle(range => range.ConfirmedClosedMarket);
        (await store.GetCoverageAsync(instrument, PriceResolution.FiveMinutes, DateTimeOffset.Parse("2026-07-23T00:05:00Z"), now))
            .Should().ContainSingle(coverage => coverage.Status == MarketDataCoverageStatus.NoBars);
    }

    private static MarketDataDeploymentContinuityService Create(
        InMemoryMarketDataStore store,
        ContinuityGateway gateway,
        DateTimeOffset now,
        MarketDataOptions options)
    {
        var clock = new FixedMarketDataClock(now);
        var metrics = new MarketDataRuntimeActivityMetrics();
        var recoveryOptions = new MarketDataRecoveryOptions();
        var recovery = new MarketDataRecoveryCoordinator(store, store, gateway, clock, recoveryOptions, metrics, NullLogger<MarketDataRecoveryCoordinator>.Instance);
        var snapshots = new FakeObjectStore();
        var publisher = new MarketDataSnapshotPublisher(
            snapshots,
            new MarketDataSnapshotValidator(),
            clock,
            Options.Create(options),
            NullLogger<MarketDataSnapshotPublisher>.Instance,
            metrics);
        return new MarketDataDeploymentContinuityService(
            store,
            new FakeHealthStore(),
            store,
            store,
            recovery,
            publisher,
            snapshots,
            snapshots,
            gateway,
            clock,
            Options.Create(options),
            new MarketDataDeploymentContinuityStore(Options.Create(options)),
            NullLogger<MarketDataDeploymentContinuityService>.Instance);
    }

    private static MarketDataDeploymentCheckpoint Checkpoint(InstrumentId instrument, DateTimeOffset now, string latestBarUtc)
        => new(
            1,
            "test-deployment",
            now,
            PriceResolution.FiveMinutes,
            [new MarketDataDeploymentCheckpointMarket(instrument.Value, DateTimeOffset.Parse(latestBarUtc))],
            new MarketDataDeploymentSnapshot("bucket", "snapshot.sqlite", "1", "hash", now, DateTimeOffset.Parse(latestBarUtc)));

    private static StoredPriceBar Stored(InstrumentId instrument, string timestampUtc)
        => StoredPriceBar.FromPriceBar(
            instrument,
            PriceResolution.FiveMinutes,
            new PriceBar(DateTimeOffset.Parse(timestampUtc), 1, 1, 1, 1, 1, 1, 1, 1, null),
            MarketDataSource.Stream);

    private static IReadOnlyList<PriceBar> Bars(DateTimeOffset fromUtc, DateTimeOffset toUtc)
        => Enumerable.Range(0, (int)((toUtc - fromUtc).TotalMinutes / 5))
            .Select(index => new PriceBar(fromUtc.AddMinutes(index * 5), 1, 1, 1, 1, 1, 1, 1, 1, null))
            .ToArray();

    private sealed class ContinuityWorkspace : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"ai-trader-continuity-{Guid.NewGuid():N}");

        public ContinuityWorkspace()
        {
            Options = new MarketDataOptions
            {
                DeploymentContinuity = new MarketDataDeploymentContinuityOptions
                {
                    CheckpointPath = Path.Combine(_root, "active.json"),
                    ReportDirectory = Path.Combine(_root, "reports"),
                    ArchiveDirectory = Path.Combine(_root, "archive"),
                    MaximumGapWindow = TimeSpan.FromMinutes(30),
                    ReadinessTimeout = TimeSpan.FromSeconds(1),
                    RepairTimeout = TimeSpan.FromSeconds(5),
                },
            };
        }

        public MarketDataOptions Options { get; }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeObjectStore : IMarketDataSnapshotObjectStore, IMarketDataObjectStore
    {
        public Task<MarketDataSnapshotObject?> GetAsync(string bucketName, string objectName, CancellationToken cancellationToken = default)
            => Task.FromResult<MarketDataSnapshotObject?>(null);

        public Task DownloadAsync(string bucketName, string objectName, string destinationPath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UploadAsync(string bucketName, string objectName, string sourcePath, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UploadAsync(string bucketName, string objectName, string sourcePath, IReadOnlyDictionary<string, string> metadata, string contentType, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class ContinuityGateway(Func<GetPricesRequest, PriceSeries> prices) : ITradingGateway
    {
        public List<GetPricesRequest> PriceRequests { get; } = [];
        public MarketStatus MarketStatus { get; init; } = MarketStatus.Tradeable;
        public Task<ITradingSession> AuthenticateAsync(CancellationToken cancellationToken = default) => Task.FromResult<ITradingSession>(new Session());
        public Task<PriceSeries> GetPricesAsync(GetPricesRequest request, CancellationToken cancellationToken = default) { PriceRequests.Add(request); return Task.FromResult(prices(request)); }
        public Task<MarketDetails> GetMarketDetailsAsync(InstrumentId instrument, CancellationToken cancellationToken = default) => Task.FromResult(new MarketDetails(instrument, instrument.Value, MarketStatus, null, null, null, null, null, null, null, null, null, null, null, null, []));
        public Task<PlaceOrderResult> PlaceMarketOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkingOrderResult> PlaceWorkingOrderAsync(CreateWorkingOrderRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ClosePositionResult> ClosePositionAsync(ClosePositionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UpdatePositionResult> UpdatePositionAsync(UpdatePositionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkingOrderResult> UpdateWorkingOrderAsync(UpdateWorkingOrderRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkingOrderResult> CancelWorkingOrderAsync(string dealId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PositionSummary>> GetOpenPositionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkingOrderSummary>> GetWorkingOrdersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MarketSearchResult>> SearchMarketsAsync(string searchTerm, int maxResults = 20, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MarketNavigationPage> BrowseMarketsAsync(string? nodeId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrderSummary>> GetOrdersAsync(OrderQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OrderSummary?> GetOrderStatusAsync(string dealReference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        private sealed record Session() : ITradingSession { public string AccountId => "demo"; public string BrokerName => "fake"; public DateTimeOffset AuthenticatedAtUtc => DateTimeOffset.UtcNow; }
    }

    private sealed class FakeHealthStore : IMarketDataHealthStore
    {
        public Task UpsertAsync(MarketDataHealthRecord health, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<MarketDataHealthRecord?> GetAsync(InstrumentId instrument, PriceResolution resolution, CancellationToken cancellationToken = default) => Task.FromResult<MarketDataHealthRecord?>(null);
    }
}
