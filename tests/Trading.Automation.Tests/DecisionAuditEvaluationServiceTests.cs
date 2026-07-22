using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.Abstractions;
using Trading.Automation.Execution;
using Trading.MarketData;
using Trading.Strategy.Shared;

public sealed class DecisionAuditEvaluationServiceTests
{
    [Fact]
    public async Task EvaluateAsync_repeated_runs_should_append_sidecars_without_changing_the_source_audit()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var instrument = new InstrumentId("CC.D.TEST.IP");
            var writer = CreateWriter(tempDirectory);
            var auditPath = Path.Combine(tempDirectory.FullName, "2026-03-12", "100000000-decision-audit.json");
            await writer.SaveAsync(
                auditPath,
                CreateAuditRecord(instrument, assessments: [], candidates: []),
                CancellationToken.None);
            var originalAuditBytes = await File.ReadAllBytesAsync(auditPath);
            var service = CreateService(writer, new InMemoryMarketDataStore());
            var request = new DecisionAuditEvaluationRequest(
                tempDirectory.FullName,
                new DateOnly(2026, 3, 12),
                PriceResolution.FiveMinutes);

            await service.EvaluateAsync(request, CancellationToken.None);
            await service.EvaluateAsync(request, CancellationToken.None);

            Directory.GetFiles(
                Path.GetDirectoryName(auditPath)!,
                "*-decision-evaluation-*.json").Should().HaveCount(2);
            (await File.ReadAllBytesAsync(auditPath)).Should().Equal(originalAuditBytes);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task EvaluateAsync_ShouldUpdateAuditRecordWithPaperOutcomeAndSummary()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var instrument = new InstrumentId("CC.D.TEST.IP");
            var store = new InMemoryMarketDataStore();
            await store.UpsertAsync(
            [
                StoredPriceBar.FromPriceBar(
                    instrument,
                    PriceResolution.FiveMinutes,
                    Bar("2026-03-12T10:00:00Z", bidLow: 99m, bidHigh: 104m, bidClose: 102m, askLow: 99.2m, askHigh: 104.2m, askClose: 102.2m),
                    MarketDataSource.Stream),
                StoredPriceBar.FromPriceBar(
                    instrument,
                    PriceResolution.FiveMinutes,
                    Bar("2026-03-12T10:05:00Z", bidLow: 101m, bidHigh: 111m, bidClose: 110m, askLow: 101.2m, askHigh: 111.2m, askClose: 110.2m),
                    MarketDataSource.Stream),
                StoredPriceBar.FromPriceBar(
                    instrument,
                    PriceResolution.FiveMinutes,
                    Bar("2026-03-12T10:10:00Z", bidLow: 109m, bidHigh: 111m, bidClose: 110m, askLow: 109.2m, askHigh: 111.2m, askClose: 110.2m),
                    MarketDataSource.Stream),
                StoredPriceBar.FromPriceBar(
                    instrument,
                    PriceResolution.FiveMinutes,
                    Bar("2026-03-12T10:15:00Z", bidLow: 109m, bidHigh: 111m, bidClose: 110m, askLow: 109.2m, askHigh: 111.2m, askClose: 110.2m),
                    MarketDataSource.Stream),
                StoredPriceBar.FromPriceBar(
                    instrument,
                    PriceResolution.FiveMinutes,
                    Bar("2026-03-12T10:20:00Z", bidLow: 109m, bidHigh: 111m, bidClose: 110m, askLow: 109.2m, askHigh: 111.2m, askClose: 110.2m),
                    MarketDataSource.Stream),
                StoredPriceBar.FromPriceBar(
                    instrument,
                    PriceResolution.FiveMinutes,
                    Bar("2026-03-12T10:25:00Z", bidLow: 109m, bidHigh: 111m, bidClose: 110m, askLow: 109.2m, askHigh: 111.2m, askClose: 110.2m),
                    MarketDataSource.Stream),
                StoredPriceBar.FromPriceBar(
                    instrument,
                    PriceResolution.FiveMinutes,
                    Bar("2026-03-12T10:30:00Z", bidLow: 109m, bidHigh: 111m, bidClose: 110m, askLow: 109.2m, askHigh: 111.2m, askClose: 110.2m),
                    MarketDataSource.Stream),
            ]);
            var writer = new DecisionAuditWriter(
                Options.Create(new PromptObservabilityOptions
                {
                    ObservabilityRootPath = tempDirectory.FullName,
                }));
            var auditPath = Path.Combine(tempDirectory.FullName, "2026-03-12", "100000000-decision-audit.json");
            await writer.SaveAsync(auditPath, CreateAuditRecord(instrument), CancellationToken.None);
            var originalAuditBytes = await File.ReadAllBytesAsync(auditPath);
            var service = new DecisionAuditEvaluationService(
                writer,
                new DecisionEvidenceSidecarWriter(writer),
                new PaperTradeOutcomeEvaluator(),
                new PaperMarketAssessmentEvaluator(),
                new MarketDataService(
                    store,
                    Options.Create(new MarketDataOptions())),
                new AuditMarketDataQualityAnalyzer(store, store));

            var report = await service.EvaluateAsync(
                new DecisionAuditEvaluationRequest(tempDirectory.FullName, new DateOnly(2026, 3, 12), PriceResolution.FiveMinutes),
                CancellationToken.None);

            report.RecordsEvaluated.Should().Be(1);
            report.CandidatesEvaluated.Should().Be(1);
            report.TargetHitCount.Should().Be(1);
            report.AssessmentsEvaluated.Should().Be(1);
            report.AssessmentDataInsufficientCount.Should().Be(1);
            report.ReportArtifact.Should().NotBeNull();
            File.Exists(report.ReportArtifact!.Path).Should().BeTrue();

            (await File.ReadAllBytesAsync(auditPath)).Should().Equal(originalAuditBytes);
            var evaluationPath = Directory.GetFiles(
                Path.GetDirectoryName(auditPath)!,
                "*-decision-evaluation-*.json").Should().ContainSingle().Which;
            var evaluation = JsonSerializer.Deserialize<DecisionEvaluationRecord>(
                await File.ReadAllTextAsync(evaluationPath),
                DecisionAuditJson.Options);
            evaluation!.PaperOutcomes.Should().ContainSingle();
            evaluation.PaperOutcomes[0].Status.Should().Be(PaperTradeOutcomeStatus.TargetHit);
            evaluation.PaperOutcomes[0].EstimatedRMultiple.Should().Be(2m);
            evaluation.MarketAssessmentOutcomes.Should().ContainSingle();
            evaluation.MarketAssessmentOutcomes[0].Status.Should().Be(PaperMarketAssessmentOutcomeStatus.DataInsufficient);
            evaluation.SourceAuditSha256.Should().MatchRegex("^[a-f0-9]{64}$");
            report.EvaluationArtifacts.Should().ContainSingle().Which.Path.Should().Be(evaluationPath);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task EvaluateAsync_ShouldKeepCandidateDataInsufficientWhenMarketDataHasGaps()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var instrument = new InstrumentId("CC.D.TEST.IP");
            var store = new InMemoryMarketDataStore();
            await store.UpsertAsync(
            [
                StoredPriceBar.FromPriceBar(
                    instrument,
                    PriceResolution.FiveMinutes,
                    Bar("2026-03-12T10:00:00Z", bidLow: 99m, bidHigh: 104m, bidClose: 102m, askLow: 99.2m, askHigh: 104.2m, askClose: 102.2m),
                    MarketDataSource.Stream),
                StoredPriceBar.FromPriceBar(
                    instrument,
                    PriceResolution.FiveMinutes,
                    Bar("2026-03-12T10:15:00Z", bidLow: 101m, bidHigh: 111m, bidClose: 110m, askLow: 101.2m, askHigh: 111.2m, askClose: 110.2m),
                    MarketDataSource.Stream),
            ]);
            var writer = new DecisionAuditWriter(
                Options.Create(new PromptObservabilityOptions
                {
                    ObservabilityRootPath = tempDirectory.FullName,
                }));
            var auditPath = Path.Combine(tempDirectory.FullName, "2026-03-12", "100000000-decision-audit.json");
            await writer.SaveAsync(auditPath, CreateAuditRecord(instrument, assessments: []), CancellationToken.None);
            var service = new DecisionAuditEvaluationService(
                writer,
                new DecisionEvidenceSidecarWriter(writer),
                new PaperTradeOutcomeEvaluator(),
                new PaperMarketAssessmentEvaluator(),
                new MarketDataService(
                    store,
                    Options.Create(new MarketDataOptions())),
                new AuditMarketDataQualityAnalyzer(store, store));

            var report = await service.EvaluateAsync(
                new DecisionAuditEvaluationRequest(tempDirectory.FullName, new DateOnly(2026, 3, 12), PriceResolution.FiveMinutes),
                CancellationToken.None);

            report.TargetHitCount.Should().Be(0);
            report.DataInsufficientCount.Should().Be(1);

            var evaluation = await LoadSingleEvaluationAsync(auditPath);
            evaluation.PaperOutcomes.Should().ContainSingle();
            evaluation.PaperOutcomes[0].Status.Should().Be(PaperTradeOutcomeStatus.DataInsufficient);
            evaluation.PaperOutcomes[0].Reason.Should().Contain("First missing range");
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task EvaluateAsync_ShouldAcceptCandidateTargetHitBeforeLaterMissingBar()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var instrument = new InstrumentId("CC.D.TEST.IP");
            var store = new InMemoryMarketDataStore();
            await store.UpsertAsync(
            [
                StoredPriceBar.FromPriceBar(
                    instrument,
                    PriceResolution.FiveMinutes,
                    Bar("2026-03-12T10:00:00Z", bidLow: 99m, bidHigh: 104m, bidClose: 102m, askLow: 99.2m, askHigh: 104.2m, askClose: 102.2m),
                    MarketDataSource.Stream),
                StoredPriceBar.FromPriceBar(
                    instrument,
                    PriceResolution.FiveMinutes,
                    Bar("2026-03-12T10:05:00Z", bidLow: 101m, bidHigh: 111m, bidClose: 110m, askLow: 101.2m, askHigh: 111.2m, askClose: 110.2m),
                    MarketDataSource.Stream),
            ]);
            var writer = CreateWriter(tempDirectory);
            var auditPath = Path.Combine(tempDirectory.FullName, "2026-03-12", "100000000-decision-audit.json");
            await writer.SaveAsync(auditPath, CreateAuditRecord(instrument, assessments: []), CancellationToken.None);
            var service = CreateService(writer, store);

            var report = await service.EvaluateAsync(
                new DecisionAuditEvaluationRequest(tempDirectory.FullName, new DateOnly(2026, 3, 12), PriceResolution.FiveMinutes),
                CancellationToken.None);

            report.TargetHitCount.Should().Be(1);
            report.DataInsufficientCount.Should().Be(0);
            report.DataQuality!.InsufficientTailWindows.Should().Be(1);

            var evaluation = await LoadSingleEvaluationAsync(auditPath);
            evaluation.PaperOutcomes[0].Status.Should().Be(PaperTradeOutcomeStatus.TargetHit);
            evaluation.PaperOutcomes[0].Reason.Should().Contain("first unsafe data-quality issue starts after the close");
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task EvaluateAsync_ShouldEvaluateAssessmentWithOneInteriorGapByDefault()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var instrument = new InstrumentId("CC.D.TEST.IP");
            var store = new InMemoryMarketDataStore();
            await store.UpsertAsync(CreateAssessmentBars(instrument, skipTimestampUtc: "2026-03-12T10:30:00Z"));
            var writer = CreateWriter(tempDirectory);
            var auditPath = Path.Combine(tempDirectory.FullName, "2026-03-12", "100000000-decision-audit.json");
            var record = CreateAuditRecord(instrument, candidates: []);
            await writer.SaveAsync(auditPath, record, CancellationToken.None);
            var service = CreateService(writer, store);

            var report = await service.EvaluateAsync(
                new DecisionAuditEvaluationRequest(tempDirectory.FullName, new DateOnly(2026, 3, 12), PriceResolution.FiveMinutes),
                CancellationToken.None);

            report.AssessmentFollowedBiasCount.Should().Be(1);
            report.AssessmentDataInsufficientCount.Should().Be(0);
            report.DataQuality!.EvaluatedWithToleratedGaps.Should().Be(1);

            var evaluation = await LoadSingleEvaluationAsync(auditPath);
            evaluation.MarketAssessmentOutcomes[0].Status.Should().Be(PaperMarketAssessmentOutcomeStatus.FollowedBias);
            evaluation.MarketAssessmentOutcomes[0].Reason.Should().Contain("small interior data gap");
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task EvaluateAsync_WithStrictData_ShouldRejectAssessmentWithInteriorGap()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var instrument = new InstrumentId("CC.D.TEST.IP");
            var store = new InMemoryMarketDataStore();
            await store.UpsertAsync(CreateAssessmentBars(instrument, skipTimestampUtc: "2026-03-12T10:30:00Z"));
            var writer = CreateWriter(tempDirectory);
            var auditPath = Path.Combine(tempDirectory.FullName, "2026-03-12", "100000000-decision-audit.json");
            await writer.SaveAsync(auditPath, CreateAuditRecord(instrument, candidates: []), CancellationToken.None);
            var service = CreateService(writer, store);

            var report = await service.EvaluateAsync(
                new DecisionAuditEvaluationRequest(
                    tempDirectory.FullName,
                    new DateOnly(2026, 3, 12),
                    PriceResolution.FiveMinutes,
                    StrictData: true),
                CancellationToken.None);

            report.AssessmentFollowedBiasCount.Should().Be(0);
            report.AssessmentDataInsufficientCount.Should().Be(1);

            var evaluation = await LoadSingleEvaluationAsync(auditPath);
            evaluation.MarketAssessmentOutcomes[0].Status.Should().Be(PaperMarketAssessmentOutcomeStatus.DataInsufficient);
            evaluation.MarketAssessmentOutcomes[0].Reason.Should().Contain("Strict data mode");
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    private static DecisionAuditRecord CreateAuditRecord(
        InstrumentId instrument,
        IReadOnlyList<DecisionAuditAssessment>? assessments = null,
        IReadOnlyList<DecisionAuditCandidate>? candidates = null)
    {
        var assessment = new DecisionAuditAssessment(
            instrument,
            "Test Market",
            72,
            TradeDirection.Buy,
            "Constructive",
            "Momentum improved",
            "");
        var candidate = new DecisionAuditCandidate(
            instrument,
            "Test Market",
            TradeDirection.Buy,
            72,
            TradeEntryMethod.Market,
            100m,
            95m,
            110m,
            2m,
            100m,
            0.2m,
            "Thesis",
            "Invalidation",
            "Why now",
            DateTimeOffset.Parse("2026-03-12T10:30:00Z"));
        assessments ??= [assessment];
        candidates ??= [candidate];

        return new DecisionAuditRecord(
            "2026-03-12/100000000-decision-audit",
            new DateOnly(2026, 3, 12),
            DateTimeOffset.Parse("2026-03-12T10:00:00Z"),
            DateTimeOffset.Parse("2026-03-12T10:01:00Z"),
            DecisionAuditDecision.PaperOnly,
            "Candidates captured for paper evaluation only. No broker order was placed.",
            new PromptAuditReference(
                Artifact("prepared.json"),
                Artifact("request.txt"),
                Artifact("envelope.json"),
                Artifact("extracted.json"),
                "gpt-test",
                "ResponsesBackground",
                "resp_test",
                "completed"),
            assessments,
            candidates,
            TradingExecutionMode.Disabled,
            [],
            null,
            null,
            new IntradayCandidateDecisionSummary(candidates.Count, 0, candidates.Count, 0, 0),
            DecisionBiasSummary.From(assessments, candidates));
    }

    private static async Task<DecisionEvaluationRecord> LoadSingleEvaluationAsync(string auditPath)
    {
        var path = Directory.GetFiles(
            Path.GetDirectoryName(auditPath)!,
            "*-decision-evaluation-*.json").Should().ContainSingle().Which;
        return JsonSerializer.Deserialize<DecisionEvaluationRecord>(
            await File.ReadAllTextAsync(path),
            DecisionAuditJson.Options)!;
    }

    private static DecisionAuditWriter CreateWriter(DirectoryInfo root)
        => new(Options.Create(new PromptObservabilityOptions
        {
            ObservabilityRootPath = root.FullName,
        }));

    private static DecisionAuditEvaluationService CreateService(
        DecisionAuditWriter writer,
        InMemoryMarketDataStore store)
        => new(
            writer,
            new DecisionEvidenceSidecarWriter(writer),
            new PaperTradeOutcomeEvaluator(),
            new PaperMarketAssessmentEvaluator(),
            new MarketDataService(
                store,
                Options.Create(new MarketDataOptions())),
            new AuditMarketDataQualityAnalyzer(store, store));

    private static IReadOnlyList<StoredPriceBar> CreateAssessmentBars(
        InstrumentId instrument,
        string skipTimestampUtc)
    {
        var skip = DateTimeOffset.Parse(skipTimestampUtc);
        var bars = new List<StoredPriceBar>();
        for (var timestamp = DateTimeOffset.Parse("2026-03-12T10:00:00Z");
             timestamp <= DateTimeOffset.Parse("2026-03-12T11:00:00Z");
             timestamp = timestamp.AddMinutes(5))
        {
            if (timestamp == skip)
            {
                continue;
            }

            var offset = (decimal)(timestamp - DateTimeOffset.Parse("2026-03-12T10:00:00Z")).TotalMinutes / 5m;
            bars.Add(StoredPriceBar.FromPriceBar(
                instrument,
                PriceResolution.FiveMinutes,
                Bar(timestamp.ToString("O"), 99m + offset, 101m + offset, 100m + offset, 99.2m + offset, 101.2m + offset, 100.2m + offset),
                MarketDataSource.Stream));
        }

        return bars;
    }

    private static PriceBar Bar(
        string timestampUtc,
        decimal bidLow,
        decimal bidHigh,
        decimal bidClose,
        decimal askLow,
        decimal askHigh,
        decimal askClose)
        => new(
            DateTimeOffset.Parse(timestampUtc),
            bidClose,
            bidHigh,
            bidLow,
            bidClose,
            askClose,
            askHigh,
            askLow,
            askClose,
            null);

    private static ArtifactReference Artifact(string path)
        => new(Path.GetFullPath(path), new Uri(Path.GetFullPath(path)).AbsoluteUri);

    private sealed class FakeTradingGateway : ITradingGateway
    {
        public Task<ITradingSession> AuthenticateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
        public Task<PriceSeries> GetPricesAsync(GetPricesRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrderSummary>> GetOrdersAsync(OrderQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OrderSummary?> GetOrderStatusAsync(string dealReference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
