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

    [Fact]
    public async Task GetOperationsByTradingDateAsync_ShouldReturnReservedOperationsForTheDay()
    {
        using var database = TestExecutionDatabase.Create();
        var service = CreateService(database.Path);
        var intent = CreateIntent();

        await service.ReserveAsync(intent);

        var operations = await service.GetOperationsByTradingDateAsync(TradingDate);

        operations.Should().ContainSingle();
        operations[0].OperationId.Should().Be(intent.DecisionId);
        operations[0].StopLevel.Should().Be(intent.StopLossPrice);
        operations[0].LimitLevel.Should().Be(intent.TakeProfitPrice);
    }

    [Fact]
    public async Task GetUnresolvedOperationsAsync_ShouldReturnReservedOperations()
    {
        using var database = TestExecutionDatabase.Create();
        var service = CreateService(database.Path);
        var intent = CreateIntent();

        await service.ReserveAsync(intent);

        var unresolved = await service.GetUnresolvedOperationsAsync();

        unresolved.Should().ContainSingle();
        unresolved[0].OperationId.Should().Be(intent.DecisionId);
        unresolved[0].State.Should().Be(ExecutionBoundaryState.Reserved);
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
