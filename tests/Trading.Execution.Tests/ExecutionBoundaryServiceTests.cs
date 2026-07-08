using FluentAssertions;
using Trading.Abstractions;
using Trading.Execution;
using Trading.Strategy.Shared;

namespace Trading.Execution.Tests;

public sealed class ExecutionBoundaryServiceTests
{
    private static readonly DateOnly TradingDate = new(2026, 03, 12);
    private static readonly DateTimeOffset ApprovedAtUtc = DateTimeOffset.Parse("2026-03-12T01:00:00Z");

    [Fact]
    public async Task ReserveAsync_WithSameDecisionTwice_ShouldCreateOneDurableRecord()
    {
        using var database = TestExecutionDatabase.Create();
        var service = CreateService(database.Path);
        var intent = CreateIntent();

        var first = await service.ReserveAsync(intent);
        var second = await service.ReserveAsync(intent);

        first.Created.Should().BeTrue();
        second.Created.Should().BeFalse();
        second.Record.DecisionId.Should().Be(intent.DecisionId);
        second.Record.DealReference.Should().Be(first.Record.DealReference);
        second.Record.State.Should().Be(ExecutionBoundaryState.Reserved);
    }

    [Fact]
    public async Task ReserveAsync_ShouldPersistRecordAcrossStoreInstances()
    {
        using var database = TestExecutionDatabase.Create();
        var intent = CreateIntent();

        await CreateService(database.Path).ReserveAsync(intent);
        var restarted = CreateService(database.Path);

        var record = await restarted.ReserveAsync(intent);

        record.Created.Should().BeFalse();
        record.Record.Intent.StopLossPrice.Should().Be(intent.StopLossPrice);
        record.Record.Intent.TakeProfitPrice.Should().Be(intent.TakeProfitPrice);
    }

    [Fact]
    public async Task ReserveAsync_WithTwoWorkers_ShouldCreateOneRecord()
    {
        using var database = TestExecutionDatabase.Create();
        var intent = CreateIntent();
        var serviceA = CreateService(database.Path);
        var serviceB = CreateService(database.Path);

        var results = await Task.WhenAll(
            serviceA.ReserveAsync(intent),
            serviceB.ReserveAsync(intent));

        results.Count(result => result.Created).Should().Be(1);
        results.Select(result => result.Record.DealReference).Distinct(StringComparer.Ordinal).Should().ContainSingle();
    }

    [Fact]
    public async Task SubmitOnceAsync_WithDuplicateProcessing_ShouldCallBrokerAtMostOnce()
    {
        using var database = TestExecutionDatabase.Create();
        var service = CreateService(database.Path);
        var intent = CreateIntent();
        var broker = new FakeBroker();

        var results = await Task.WhenAll(
            service.SubmitOnceAsync(intent, broker.SubmitAcceptedAsync),
            service.SubmitOnceAsync(intent, broker.SubmitAcceptedAsync));

        broker.SubmissionCount.Should().Be(1);
        results.Select(result => result.State).Should().Contain(ExecutionBoundaryState.Confirmed);
        var final = await service.ReconcileAsync(intent.DecisionId, broker.GetStatusAsync);
        final!.State.Should().Be(ExecutionBoundaryState.Confirmed);
        final.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task SubmitOnceAsync_WhenPreSubmissionCheckFails_ShouldNotCallBroker()
    {
        using var database = TestExecutionDatabase.Create();
        var service = CreateService(database.Path);
        var intent = CreateIntent();
        var broker = new FakeBroker();

        var record = await service.SubmitOnceAsync(
            intent,
            broker.SubmitAcceptedAsync,
            (_, _) => throw new InvalidOperationException("Configuration is not armed."));

        broker.SubmissionCount.Should().Be(0);
        record.State.Should().Be(ExecutionBoundaryState.FailedBeforeSubmission);
        record.LastError.Should().Be("Configuration is not armed.");
    }

    [Fact]
    public async Task SubmitOnceAsync_AfterReservationRestart_ShouldSubmitOnce()
    {
        using var database = TestExecutionDatabase.Create();
        var intent = CreateIntent();
        await CreateService(database.Path).ReserveAsync(intent);
        var restarted = CreateService(database.Path);
        var broker = new FakeBroker();

        var record = await restarted.SubmitOnceAsync(intent, broker.SubmitAcceptedAsync);

        broker.SubmissionCount.Should().Be(1);
        record.State.Should().Be(ExecutionBoundaryState.Confirmed);
    }

    [Fact]
    public async Task SubmitOnceAsync_WhenRestartFindsSubmittingRecord_ShouldReconcileInsteadOfResubmitting()
    {
        using var database = TestExecutionDatabase.Create();
        var intent = CreateIntent();
        var firstStore = new SqliteExecutionBoundaryStore(database.Path);
        var dealReference = new ExecutionDealReferenceFactory().CreateOpenReference(intent.DecisionId);
        await firstStore.ReserveAsync(intent, dealReference, ApprovedAtUtc);
        var lease = await firstStore.TryBeginSubmissionAsync(intent.DecisionId, ApprovedAtUtc.AddSeconds(1));
        lease.Should().NotBeNull();

        var restarted = CreateService(database.Path);
        var broker = new FakeBroker();
        broker.SeedStatus(dealReference, new OrderSummary(
            dealReference,
            "DEAL-1",
            intent.Instrument,
            intent.Direction,
            1m,
            OrderStatus.Open,
            "Confirmed after restart.",
            ApprovedAtUtc.AddSeconds(2)));

        var duplicateRun = await restarted.SubmitOnceAsync(intent, broker.SubmitAcceptedAsync);
        var reconciled = await restarted.ReconcileAsync(intent.DecisionId, broker.GetStatusAsync);

        broker.SubmissionCount.Should().Be(0);
        duplicateRun.State.Should().Be(ExecutionBoundaryState.Submitting);
        reconciled!.State.Should().Be(ExecutionBoundaryState.Confirmed);
        reconciled.DealId.Should().Be("DEAL-1");
    }

    [Fact]
    public async Task SubmitOnceAsync_WhenBrokerSubmissionThrows_ShouldPersistOutcomeUncertain()
    {
        using var database = TestExecutionDatabase.Create();
        var service = CreateService(database.Path);
        var intent = CreateIntent();
        var broker = new FakeBroker();

        var record = await service.SubmitOnceAsync(intent, broker.ThrowAfterPossibleAcceptanceAsync);

        broker.SubmissionCount.Should().Be(1);
        record.State.Should().Be(ExecutionBoundaryState.OutcomeUncertain);
        record.LastError.Should().Contain("timed out");
    }

    [Fact]
    public async Task SubmitOnceAsync_WhenBrokerRejects_ShouldNotRetryRejectedDecision()
    {
        using var database = TestExecutionDatabase.Create();
        var service = CreateService(database.Path);
        var intent = CreateIntent();
        var broker = new FakeBroker();

        var rejected = await service.SubmitOnceAsync(intent, broker.SubmitRejectedAsync);
        var duplicate = await service.SubmitOnceAsync(intent, broker.SubmitAcceptedAsync);

        broker.SubmissionCount.Should().Be(1);
        rejected.State.Should().Be(ExecutionBoundaryState.BrokerRejected);
        duplicate.State.Should().Be(ExecutionBoundaryState.BrokerRejected);
    }

    [Fact]
    public async Task AttachDecisionAuditArtifactAsync_ShouldLinkRecordBackToAuditFile()
    {
        using var database = TestExecutionDatabase.Create();
        var service = CreateService(database.Path);
        var intent = CreateIntent();
        var reservation = await service.ReserveAsync(intent);
        var auditPath = Path.Combine(database.Directory, "decision-audit.json");
        await File.WriteAllTextAsync(auditPath, "{}");

        var linked = await service.AttachDecisionAuditArtifactAsync(reservation.Record.DecisionId, auditPath);

        linked.Should().NotBeNull();
        linked!.SourceDecisionAuditPath.Should().Be(Path.GetFullPath(auditPath));
    }

    private static ExecutionBoundaryService CreateService(string databasePath)
        => new(
            new SqliteExecutionBoundaryStore(databasePath),
            new ExecutionDealReferenceFactory(),
            new FakeClock(ApprovedAtUtc));

    private static ExecutionReadyTradeIntent CreateIntent(string decisionId = "dec_test")
        => new(
            decisionId,
            "2026-03-12/010000000-decision-audit",
            TradingDate,
            new InstrumentId("CC.D.TEST.IP"),
            "Test Market",
            TradeDirection.Buy,
            TradeEntryMethod.Market,
            100m,
            95m,
            110m,
            ApprovedAtUtc.AddMinutes(30),
            "BrokerMinimum",
            ApprovedAtUtc,
            ["Candidate passed deterministic phase-one shadow checks."],
            new ShadowDecisionRulesSnapshot(
                TradingExecutionMode.Shadow,
                [new InstrumentId("CC.D.TEST.IP")],
                [TradeEntryMethod.Market],
                70,
                2m,
                0.20m,
                0.25m,
                TimeSpan.FromMinutes(20),
                TimeSpan.FromMinutes(30),
                "BrokerMinimum"),
            new ShadowDecisionContextSnapshot(
                "Australia/Melbourne",
                TradingDate,
                TradingDate,
                ApprovedAtUtc,
                ApprovedAtUtc.AddMinutes(-1),
                100m,
                0.2m,
                1,
                "Mixed"),
            ["Shadow mode records intent only; no broker order is submitted."]);

    private sealed class FakeBroker
    {
        private readonly Dictionary<string, OrderSummary> _statuses = [];
        private int _submissionCount;

        public int SubmissionCount => _submissionCount;

        public async Task<PlaceOrderResult> SubmitAcceptedAsync(
            string dealReference,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _submissionCount);
            await Task.Delay(20, cancellationToken);
            var result = new PlaceOrderResult(
                dealReference,
                "DEAL-1",
                OrderStatus.Open,
                "Opened.",
                ApprovedAtUtc.AddSeconds(2));
            SeedStatus(dealReference, new OrderSummary(
                dealReference,
                result.DealId,
                new InstrumentId("CC.D.TEST.IP"),
                TradeDirection.Buy,
                1m,
                result.Status,
                result.Message,
                result.TimestampUtc));
            return result;
        }

        public Task<PlaceOrderResult> SubmitRejectedAsync(
            string dealReference,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _submissionCount);
            return Task.FromResult(new PlaceOrderResult(
                dealReference,
                null,
                OrderStatus.Rejected,
                "Broker rejected the order.",
                ApprovedAtUtc.AddSeconds(2)));
        }

        public Task<PlaceOrderResult> ThrowAfterPossibleAcceptanceAsync(
            string dealReference,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _submissionCount);
            throw new TimeoutException($"Broker request for {dealReference} timed out.");
        }

        public Task<OrderSummary?> GetStatusAsync(string dealReference, CancellationToken cancellationToken)
            => Task.FromResult(_statuses.GetValueOrDefault(dealReference));

        public void SeedStatus(string dealReference, OrderSummary status)
            => _statuses[dealReference] = status;
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
