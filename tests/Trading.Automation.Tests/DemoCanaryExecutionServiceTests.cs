using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.Prompts.IntradayOpportunityReview;
using Trading.Abstractions;
using Trading.Automation.Configuration;
using Trading.Automation.Execution;
using Trading.Execution;
using Trading.Strategy.Shared;

public sealed class DemoCanaryExecutionServiceTests
{
    private static readonly InstrumentId TestInstrument = new("CC.D.TEST.IP");
    private static readonly DateOnly TradingDate = new(2026, 03, 12);
    private static readonly DateTimeOffset ApprovedAtUtc = DateTimeOffset.Parse("2026-03-12T10:00:00Z");
    private const string DemoBaseUrl = "https://demo-api.ig.com/gateway/deal";
    private const string DemoAccountId = "DEMO-ACCOUNT-1";

    [Fact]
    public async Task ExecuteAsync_WithHealthyDemoSetup_ShouldSubmitProtectedMarketOrderAndPersistSnapshot()
    {
        await using var harness = await DemoCanaryHarness.CreateAsync();

        var snapshot = await harness.Service.ExecuteAsync(harness.SubmitResult);

        snapshot.Should().NotBeNull();
        snapshot!.ProtectionVerified.Should().BeTrue();
        snapshot.ProtectionAmended.Should().BeFalse();
        harness.Gateway.MarketOrderRequests.Should().ContainSingle();
        harness.Gateway.MarketOrderRequests[0].StopLevel.Should().Be(95m);
        harness.Gateway.MarketOrderRequests[0].LimitLevel.Should().Be(110m);

        var record = await harness.AuditWriter.LoadAsync(harness.AuditPath, CancellationToken.None);
        record.DemoExecution.Should().NotBeNull();
        record.DemoExecution!.Outcome.Should().Contain("confirmed stop and target protection");
        record.DemoExecution.DealId.Should().Be(snapshot.DealId);
    }

    [Fact]
    public async Task ExecuteAsync_WithPriorSameDayExecution_ShouldFailClosedWithoutSubmitting()
    {
        await using var harness = await DemoCanaryHarness.CreateAsync(addPriorSameDayReservation: true);

        var snapshot = await harness.Service.ExecuteAsync(harness.SubmitResult);

        snapshot.Should().NotBeNull();
        snapshot!.Outcome.Should().Contain("already has 1 prior execution operation");
        harness.Gateway.MarketOrderRequests.Should().BeEmpty();
        harness.Gateway.UpdatePositionRequests.Should().BeEmpty();
        harness.Gateway.ClosePositionRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithMismatchedApprovedAccount_ShouldFailClosedWithoutSubmitting()
    {
        await using var harness = await DemoCanaryHarness.CreateAsync(approvedAccountId: "OTHER-ACCOUNT");

        var snapshot = await harness.Service.ExecuteAsync(harness.SubmitResult);

        snapshot.Should().NotBeNull();
        snapshot!.Outcome.Should().Contain("approved demo account");
        harness.Gateway.MarketOrderRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithMismatchedApprovedBaseUrl_ShouldFailClosedWithoutSubmitting()
    {
        await using var harness = await DemoCanaryHarness.CreateAsync(configuredBaseUrl: "https://live-api.ig.com/gateway/deal");

        var snapshot = await harness.Service.ExecuteAsync(harness.SubmitResult);

        snapshot.Should().NotBeNull();
        snapshot!.Outcome.Should().Contain("approved demo endpoint");
        harness.Gateway.MarketOrderRequests.Should().BeEmpty();
        harness.Gateway.AuthenticateCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithKillSwitchEngaged_ShouldFailClosedWithoutSubmitting()
    {
        await using var harness = await DemoCanaryHarness.CreateAsync(killSwitchEngaged: true);

        var snapshot = await harness.Service.ExecuteAsync(harness.SubmitResult);

        snapshot.Should().NotBeNull();
        snapshot!.Outcome.Should().Contain("kill switch");
        harness.Gateway.MarketOrderRequests.Should().BeEmpty();
        harness.Gateway.AuthenticateCalls.Should().Be(0);
    }

    private sealed class DemoCanaryHarness : IAsyncDisposable
    {
        private DemoCanaryHarness(
            TemporaryDirectory temporaryDirectory,
            DecisionAuditWriter auditWriter,
            DemoCanaryExecutionService service,
            FakeDemoTradingGateway gateway,
            string auditPath,
            IntradayOpportunitySubmitResult submitResult)
        {
            TemporaryDirectory = temporaryDirectory;
            AuditWriter = auditWriter;
            Service = service;
            Gateway = gateway;
            AuditPath = auditPath;
            SubmitResult = submitResult;
        }

        public TemporaryDirectory TemporaryDirectory { get; }

        public DecisionAuditWriter AuditWriter { get; }

        public DemoCanaryExecutionService Service { get; }

        public FakeDemoTradingGateway Gateway { get; }

        public string AuditPath { get; }

        public IntradayOpportunitySubmitResult SubmitResult { get; }

        public static async Task<DemoCanaryHarness> CreateAsync(
            string? approvedAccountId = null,
            string? configuredBaseUrl = null,
            bool armed = true,
            bool killSwitchEngaged = false,
            bool addPriorSameDayReservation = false)
        {
            var tempDirectory = new TemporaryDirectory();
            var auditWriter = new DecisionAuditWriter(
                Options.Create(new PromptObservabilityOptions
                {
                    ObservabilityRootPath = tempDirectory.Path,
                }));

            var gateway = new FakeDemoTradingGateway();
            var storePath = Path.Combine(tempDirectory.Path, "execution.sqlite");
            var executionStore = new SqliteExecutionBoundaryStore(storePath);
            var executionBoundaryService = new ExecutionBoundaryService(
                executionStore,
                new ExecutionDealReferenceFactory(),
                new FakeClock(ApprovedAtUtc));
            var executionSubmissionService = new ExecutionSubmissionService(
                executionStore,
                new ExecutionDealReferenceFactory(),
                new FakeClock(ApprovedAtUtc),
                gateway);

            var intent = CreateIntent();
            var reservation = await executionBoundaryService.ReserveAsync(intent);

            if (addPriorSameDayReservation)
            {
                var priorReservation = await executionBoundaryService.ReserveAsync(CreateIntent("dec_other"));
                await executionStore.CompleteOperationAttemptAsync(
                    new ExecutionOperationAttemptCompletion(
                        priorReservation.Record.DecisionId,
                        1,
                        ExecutionBoundaryState.Confirmed,
                        ApprovedAtUtc,
                        priorReservation.Record.DealReference,
                        "DEAL-OTHER",
                        OrderStatus.Open,
                        null,
                        null));
            }

            var auditPath = Path.Combine(tempDirectory.Path, "audit.json");
            await auditWriter.SaveAsync(auditPath, CreateAuditRecord(), CancellationToken.None);

            var prepared = CreatePreparation(tempDirectory.Path);
            var workflowResult = CreateWorkflowResult(intent);
            var batch = CreateBatch(workflowResult);
            var submitResult = new IntradayOpportunitySubmitResult(
                prepared,
                new IntradayOpportunityExecutionArtifacts(
                    new ArtifactReference(Path.Combine(tempDirectory.Path, "prompt-envelope.json"), "file:///prompt-envelope.json"),
                    new ArtifactReference(Path.Combine(tempDirectory.Path, "extracted.json"), "file:///extracted.json"),
                    [],
                    new ArtifactReference(auditPath, new Uri(Path.GetFullPath(auditPath)).AbsoluteUri)),
                batch,
                workflowResult,
                ExecutionBoundarySnapshot.From(reservation.Record));

            var automationOptions = Options.Create(new AutomationOptions
            {
                Execution = new ExecutionOptions
                {
                    Mode = TradingExecutionMode.Demo,
                    Demo = new DemoExecutionOptions
                    {
                        Armed = armed,
                        KillSwitchEngaged = killSwitchEngaged,
                        ApprovedBaseUrl = DemoBaseUrl,
                        ApprovedAccountId = approvedAccountId ?? DemoAccountId,
                        AllowedInstruments = [TestInstrument.Value],
                    },
                },
            });

            var igClientOptions = Options.Create(new Ig.Trading.Sdk.Configuration.IgClientOptions
            {
                BaseUrl = configuredBaseUrl ?? DemoBaseUrl,
                ApiKey = "test-api-key",
                Identifier = "test-user",
                Password = "test-password",
                AccountId = DemoAccountId,
            });

            var service = new DemoCanaryExecutionService(
                automationOptions,
                igClientOptions,
                gateway,
                executionSubmissionService,
                executionBoundaryService,
                auditWriter,
                new FakeClock(ApprovedAtUtc),
                NullLogger<DemoCanaryExecutionService>.Instance);

            return new DemoCanaryHarness(tempDirectory, auditWriter, service, gateway, auditPath, submitResult);
        }

        public ValueTask DisposeAsync()
            => TemporaryDirectory.DisposeAsync();
    }

    private static IntradayOpportunityPreparationDocument CreatePreparation(string rootPath)
        => new(
            TradingDate,
            ApprovedAtUtc,
            "intraday-opportunity-review",
            new IntradayOpportunityReviewInput(
                TradingDate,
                ApprovedAtUtc.AddMinutes(-60),
                ApprovedAtUtc,
                1,
                4,
                "Australia/Melbourne",
                "Daily plan",
                "Watched markets",
                "No events",
                TradingDate,
                ApprovedAtUtc),
            "request",
            [],
            [],
            new ArtifactReference(Path.Combine(rootPath, "prepared.json"), "file:///prepared.json"),
            new ArtifactReference(Path.Combine(rootPath, "request.txt"), "file:///request.txt"));

    private static IntradayOpportunityReviewResult CreateWorkflowResult(ExecutionReadyTradeIntent intent)
    {
        var candidate = new IntradayOpportunityCandidate(
            TestInstrument,
            "Test Market",
            TradeDirection.Buy,
            80,
            TradeEntryMethod.Market,
            intent.ExpectedEntryPrice,
            intent.StopLossPrice,
            intent.TakeProfitPrice,
            2m,
            intent.ExpectedEntryPrice,
            0.2m,
            "Thesis",
            "Invalidation",
            "Why now",
            ApprovedAtUtc.AddMinutes(30));

        var decision = new IntradayCandidateDecision(
            "dec_test",
            candidate.Instrument,
            candidate.Direction,
            candidate.EntryMethod,
            candidate.OpportunityScore,
            IntradayCandidateDecisionStatus.ApprovedForShadowExecution,
            [IntradayCandidateDecisionReason.Approved],
            2m,
            0.04m,
            0m,
            "Approved.",
            intent);

        return new IntradayOpportunityReviewResult(
            TradingDate,
            [],
            [candidate],
            TradingExecutionMode.Demo,
            [decision],
            intent,
            new IntradayCandidateDecisionSummary(1, 1, 0, 0, 0),
            ApprovedAtUtc,
            "Validated intraday opportunity batch. Selected demo canary intent dec_test.");
    }

    private static IntradayOpportunityBatch CreateBatch(IntradayOpportunityReviewResult workflowResult)
        => new(
            workflowResult.TradingDate,
            ApprovedAtUtc,
            ApprovedAtUtc.AddMinutes(-60),
            ApprovedAtUtc,
            workflowResult.MarketAssessments,
            workflowResult.CandidateOpportunities,
            null,
            "2026-03-12/100000000-decision-audit");

    private static DecisionAuditRecord CreateAuditRecord()
        => new(
            "2026-03-12/100000000-decision-audit",
            TradingDate,
            ApprovedAtUtc,
            ApprovedAtUtc,
            DecisionAuditDecision.NoCandidate,
            "Placeholder audit.",
            new PromptAuditReference(
                new ArtifactReference("prepared.json", "file:///prepared.json"),
                new ArtifactReference("request.txt", "file:///request.txt"),
                new ArtifactReference("envelope.json", "file:///envelope.json"),
                new ArtifactReference("extracted.json", "file:///extracted.json"),
                "gpt-test",
                "ResponsesBackground",
                "resp_test",
                "completed"),
            [],
            [],
            TradingExecutionMode.Demo,
            [],
            null,
            null,
            new IntradayCandidateDecisionSummary(0, 0, 0, 0, 0),
            [],
            [],
            DecisionBiasSummary.From([], []));

    private static ExecutionReadyTradeIntent CreateIntent(string decisionId = "dec_test")
        => new(
            decisionId,
            "2026-03-12/100000000-decision-audit",
            TradingDate,
            TestInstrument,
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
                TradingExecutionMode.Demo,
                [TestInstrument],
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

    private sealed class FakeDemoTradingGateway : ITradingGateway
    {
        private readonly List<PositionSummary> _openPositions = [];
        private readonly List<WorkingOrderSummary> _workingOrders = [];

        public int AuthenticateCalls { get; private set; }

        public List<PlaceOrderRequest> MarketOrderRequests { get; } = [];

        public List<UpdatePositionRequest> UpdatePositionRequests { get; } = [];

        public List<ClosePositionRequest> ClosePositionRequests { get; } = [];

        public Task<ITradingSession> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            AuthenticateCalls++;
            return Task.FromResult<ITradingSession>(new FakeTradingSession());
        }

        public Task<PlaceOrderResult> PlaceMarketOrderAsync(
            PlaceOrderRequest request,
            CancellationToken cancellationToken = default)
        {
            MarketOrderRequests.Add(request);
            _openPositions.Add(new PositionSummary(
                "DEAL-1",
                request.Instrument,
                request.Direction,
                request.Size,
                "USD",
                ApprovedAtUtc,
                request.StopLevel,
                request.LimitLevel,
                null,
                null));

            return Task.FromResult(new PlaceOrderResult(
                request.DealReference ?? "OPEN-REF",
                "DEAL-1",
                OrderStatus.Open,
                "Opened.",
                ApprovedAtUtc));
        }

        public Task<WorkingOrderResult> PlaceWorkingOrderAsync(
            CreateWorkingOrderRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkingOrderResult("WO-REF", "WO-1", OrderStatus.Accepted, "Created.", ApprovedAtUtc));

        public Task<ClosePositionResult> ClosePositionAsync(
            ClosePositionRequest request,
            CancellationToken cancellationToken = default)
        {
            ClosePositionRequests.Add(request);
            _openPositions.RemoveAll(position => string.Equals(position.DealId, request.DealId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(new ClosePositionResult(request.DealReference ?? "CLOSE-REF", request.DealId, OrderStatus.Closed, "Closed.", ApprovedAtUtc));
        }

        public Task<UpdatePositionResult> UpdatePositionAsync(
            UpdatePositionRequest request,
            CancellationToken cancellationToken = default)
        {
            UpdatePositionRequests.Add(request);
            for (var index = 0; index < _openPositions.Count; index++)
            {
                var current = _openPositions[index];
                if (!string.Equals(current.DealId, request.DealId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _openPositions[index] = current with
                {
                    StopLevel = request.StopLevel ?? current.StopLevel,
                    LimitLevel = request.LimitLevel ?? current.LimitLevel,
                };
                break;
            }

            return Task.FromResult(new UpdatePositionResult("AMEND-REF", request.DealId, OrderStatus.Accepted, "Updated.", ApprovedAtUtc));
        }

        public Task<WorkingOrderResult> UpdateWorkingOrderAsync(
            UpdateWorkingOrderRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkingOrderResult("WO-UPD-REF", request.DealId, OrderStatus.Accepted, "Updated.", ApprovedAtUtc));

        public Task<WorkingOrderResult> CancelWorkingOrderAsync(
            string dealId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkingOrderResult("WO-CANCEL-REF", dealId, OrderStatus.Accepted, "Cancelled.", ApprovedAtUtc));

        public Task<IReadOnlyList<PositionSummary>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PositionSummary>>(_openPositions.ToArray());

        public Task<IReadOnlyList<WorkingOrderSummary>> GetWorkingOrdersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WorkingOrderSummary>>(_workingOrders.ToArray());

        public Task<IReadOnlyList<MarketSearchResult>> SearchMarketsAsync(
            string searchTerm,
            int maxResults = 20,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MarketSearchResult>>([]);

        public Task<MarketDetails> GetMarketDetailsAsync(
            InstrumentId instrument,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new MarketDetails(
                instrument,
                "Test Market",
                MarketStatus.Tradeable,
                "DFB",
                null,
                "USD",
                100m,
                100.2m,
                1m,
                "CONTRACTS",
                true,
                true,
                false,
                true,
                new MarketDealingRulesSummary(
                    new MarketRuleDistanceSummary(1m, "POINTS"),
                    null,
                    null,
                    new MarketRuleDistanceSummary(1m, "POINTS"),
                    null,
                    "AVAILABLE_DEFAULT_ON",
                    null),
                ["MARKET"]));

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
            => Task.FromResult<OrderSummary?>(new OrderSummary(
                dealReference,
                "DEAL-1",
                TestInstrument,
                TradeDirection.Buy,
                1m,
                OrderStatus.Open,
                "Opened.",
                ApprovedAtUtc));
    }

    private sealed class FakeTradingSession : ITradingSession
    {
        public string AccountId => DemoAccountId;

        public string BrokerName => "IG";

        public DateTimeOffset AuthenticatedAtUtc => ApprovedAtUtc;
    }

    private sealed class FakeClock : IExecutionClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class TemporaryDirectory : IAsyncDisposable
    {
        public TemporaryDirectory()
        {
            DirectoryInfo = Directory.CreateTempSubdirectory();
        }

        public DirectoryInfo DirectoryInfo { get; }

        public string Path => DirectoryInfo.FullName;

        public ValueTask DisposeAsync()
        {
            if (DirectoryInfo.Exists)
            {
                DirectoryInfo.Delete(true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
