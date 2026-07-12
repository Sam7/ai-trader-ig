using FluentAssertions;
using Trading.Abstractions;

namespace Trading.Execution.Tests;

public sealed class ExecutionSubmissionServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-03-12T01:00:00Z");

    [Fact]
    public async Task SubmitMarketOrderAsync_WithSameManualOperationId_ShouldSubmitBrokerOnce()
    {
        using var database = TestExecutionDatabase.Create();
        var gateway = new FakeTradingGateway();
        var service = CreateService(database.Path, gateway);
        var request = new PlaceOrderRequest(
            new InstrumentId("CC.D.TEST.IP"),
            TradeDirection.Buy,
            1m,
            StopLevel: 95m,
            LimitLevel: 110m);

        var first = await service.SubmitMarketOrderAsync("manual-open-1", ExecutionOperationSource.ManualCli, request);
        var second = await service.SubmitMarketOrderAsync("manual-open-1", ExecutionOperationSource.ManualCli, request);

        gateway.MarketOrderRequests.Should().ContainSingle();
        gateway.MarketOrderRequests[0].DealReference.Should().MatchRegex("^[A-Z0-9]{1,30}$");
        gateway.MarketOrderRequests[0].StopLevel.Should().Be(95m);
        gateway.MarketOrderRequests[0].LimitLevel.Should().Be(110m);
        first.Record.State.Should().Be(ExecutionBoundaryState.Confirmed);
        first.Record.StopLevel.Should().Be(95m);
        first.Record.LimitLevel.Should().Be(110m);
        second.Record.AttemptCount.Should().Be(1);
        second.Status.Should().Be(OrderStatus.Open);
        second.DealReference.Should().Be(first.DealReference);
    }

    [Fact]
    public async Task SubmitMarketOrderAsync_WithDifferentManualOperationIds_ShouldSubmitEachOperation()
    {
        using var database = TestExecutionDatabase.Create();
        var gateway = new FakeTradingGateway();
        var service = CreateService(database.Path, gateway);
        var request = new PlaceOrderRequest(
            new InstrumentId("CC.D.TEST.IP"),
            TradeDirection.Buy,
            1m,
            StopLevel: 95m,
            LimitLevel: 110m);

        await service.SubmitMarketOrderAsync("manual-open-1", ExecutionOperationSource.ManualCli, request);
        await service.SubmitMarketOrderAsync("manual-open-2", ExecutionOperationSource.ManualCli, request);

        gateway.MarketOrderRequests.Should().HaveCount(2);
    }

    [Fact]
    public async Task SubmitClosePositionAsync_ShouldPassDeterministicDealReferenceAndPersistRelatedDeal()
    {
        using var database = TestExecutionDatabase.Create();
        var gateway = new FakeTradingGateway();
        var service = CreateService(database.Path, gateway);

        var result = await service.SubmitClosePositionAsync(
            "manual-close-1",
            ExecutionOperationSource.ManualCli,
            new ClosePositionRequest("OPEN-DEAL-1", 0.5m));

        gateway.ClosePositionRequests.Should().ContainSingle();
        gateway.ClosePositionRequests[0].DealReference.Should().Be(result.DealReference);
        result.Record.Kind.Should().Be(ExecutionOperationKind.PositionClose);
        result.Record.RelatedDealId.Should().Be("OPEN-DEAL-1");
        result.Record.State.Should().Be(ExecutionBoundaryState.Closed);
    }

    [Fact]
    public async Task SubmitWorkingOrderMutations_ShouldPersistBrokerReturnedReferences()
    {
        using var database = TestExecutionDatabase.Create();
        var gateway = new FakeTradingGateway();
        var service = CreateService(database.Path, gateway);

        var created = await service.SubmitCreateWorkingOrderAsync(
            "manual-working-create",
            ExecutionOperationSource.ManualCli,
            new CreateWorkingOrderRequest(
                new InstrumentId("CC.D.TEST.IP"),
                TradeDirection.Buy,
                WorkingOrderType.Limit,
                1m,
                100m,
                WorkingOrderTimeInForce.GoodTillCancelled));
        var updated = await service.SubmitUpdateWorkingOrderAsync(
            "manual-working-update",
            ExecutionOperationSource.ManualCli,
            new UpdateWorkingOrderRequest("WO-DEAL-1", 101m));
        var cancelled = await service.SubmitCancelWorkingOrderAsync(
            "manual-working-cancel",
            ExecutionOperationSource.ManualCli,
            "WO-DEAL-1");

        created.DealReference.Should().Be("WO-CREATE-REF");
        created.Record.DealReference.Should().Be("WO-CREATE-REF");
        updated.DealReference.Should().Be("WO-UPDATE-REF");
        updated.Record.RelatedDealId.Should().Be("WO-DEAL-1");
        cancelled.DealReference.Should().Be("WO-CANCEL-REF");
        cancelled.Record.RelatedDealId.Should().Be("WO-DEAL-1");
    }

    private static ExecutionSubmissionService CreateService(string databasePath, ITradingGateway gateway)
        => new(
            new SqliteExecutionBoundaryStore(databasePath),
            new ExecutionDealReferenceFactory(),
            new FakeClock(Now),
            gateway);

    private sealed class FakeTradingGateway : ITradingGateway
    {
        public List<PlaceOrderRequest> MarketOrderRequests { get; } = [];

        public List<ClosePositionRequest> ClosePositionRequests { get; } = [];

        public Task<ITradingSession> AuthenticateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<ITradingSession>(new FakeTradingSession());

        public Task<PlaceOrderResult> PlaceMarketOrderAsync(
            PlaceOrderRequest request,
            CancellationToken cancellationToken = default)
        {
            MarketOrderRequests.Add(request);
            return Task.FromResult(new PlaceOrderResult(
                request.DealReference ?? "MARKET-REF",
                "MARKET-DEAL",
                OrderStatus.Open,
                "Opened.",
                Now));
        }

        public Task<WorkingOrderResult> PlaceWorkingOrderAsync(
            CreateWorkingOrderRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkingOrderResult("WO-CREATE-REF", "WO-DEAL-1", OrderStatus.Accepted, "Created.", Now));

        public Task<ClosePositionResult> ClosePositionAsync(
            ClosePositionRequest request,
            CancellationToken cancellationToken = default)
        {
            ClosePositionRequests.Add(request);
            return Task.FromResult(new ClosePositionResult(
                request.DealReference ?? "CLOSE-REF",
                request.DealId,
                OrderStatus.Closed,
                "Closed.",
                Now));
        }

        public Task<UpdatePositionResult> UpdatePositionAsync(
            UpdatePositionRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new UpdatePositionResult("POSITION-UPDATE-REF", request.DealId, OrderStatus.Accepted, "Updated.", Now));

        public Task<WorkingOrderResult> UpdateWorkingOrderAsync(
            UpdateWorkingOrderRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkingOrderResult("WO-UPDATE-REF", request.DealId, OrderStatus.Accepted, "Updated.", Now));

        public Task<WorkingOrderResult> CancelWorkingOrderAsync(
            string dealId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkingOrderResult("WO-CANCEL-REF", dealId, OrderStatus.Accepted, "Cancelled.", Now));

        public Task<IReadOnlyList<PositionSummary>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PositionSummary>>([]);

        public Task<IReadOnlyList<WorkingOrderSummary>> GetWorkingOrdersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WorkingOrderSummary>>([]);

        public Task<IReadOnlyList<MarketSearchResult>> SearchMarketsAsync(
            string searchTerm,
            int maxResults = 20,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MarketSearchResult>>([]);

        public Task<MarketDetails> GetMarketDetailsAsync(
            InstrumentId instrument,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MarketNavigationPage> BrowseMarketsAsync(
            string? nodeId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new MarketNavigationPage(nodeId, "Root", [], []));

        public Task<PriceSeries> GetPricesAsync(
            GetPricesRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PriceSeries(request.Instrument, request.Resolution, []));

        public Task<IReadOnlyList<OrderSummary>> GetOrdersAsync(
            OrderQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderSummary>>([]);

        public Task<OrderSummary?> GetOrderStatusAsync(
            string dealReference,
            CancellationToken cancellationToken = default)
            => Task.FromResult<OrderSummary?>(null);
    }

    private sealed class FakeTradingSession : ITradingSession
    {
        public string AccountId => "demo-account";

        public string BrokerName => "Fake";

        public DateTimeOffset AuthenticatedAtUtc => Now;
    }

    private sealed class FakeClock : IExecutionClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class TestExecutionDatabase : IDisposable
    {
        private TestExecutionDatabase(string directory)
        {
            Directory = directory;
            Path = System.IO.Path.Combine(directory, "execution-boundary.sqlite");
        }

        public string Directory { get; }

        public string Path { get; }

        public static TestExecutionDatabase Create()
            => new(System.IO.Directory.CreateTempSubdirectory().FullName);

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
