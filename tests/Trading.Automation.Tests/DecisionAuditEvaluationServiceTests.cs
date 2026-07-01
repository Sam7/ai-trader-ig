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
            var service = new DecisionAuditEvaluationService(
                writer,
                new PaperTradeOutcomeEvaluator(),
                new PaperMarketAssessmentEvaluator(),
                new MarketDataService(
                    store,
                    new FakeTradingGateway(),
                    Options.Create(new MarketDataOptions { BackfillEnabled = false }),
                    NullLogger<MarketDataService>.Instance));

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

            var updated = JsonSerializer.Deserialize<DecisionAuditRecord>(
                await File.ReadAllTextAsync(auditPath),
                DecisionAuditJson.Options);
            updated!.PaperOutcomes.Should().ContainSingle();
            updated.PaperOutcomes[0].Status.Should().Be(PaperTradeOutcomeStatus.TargetHit);
            updated.PaperOutcomes[0].EstimatedRMultiple.Should().Be(2m);
            updated.MarketAssessmentOutcomes.Should().ContainSingle();
            updated.MarketAssessmentOutcomes[0].Status.Should().Be(PaperMarketAssessmentOutcomeStatus.DataInsufficient);
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
            await writer.SaveAsync(auditPath, CreateAuditRecord(instrument), CancellationToken.None);
            var service = new DecisionAuditEvaluationService(
                writer,
                new PaperTradeOutcomeEvaluator(),
                new PaperMarketAssessmentEvaluator(),
                new MarketDataService(
                    store,
                    new FakeTradingGateway(),
                    Options.Create(new MarketDataOptions { BackfillEnabled = false }),
                    NullLogger<MarketDataService>.Instance));

            var report = await service.EvaluateAsync(
                new DecisionAuditEvaluationRequest(tempDirectory.FullName, new DateOnly(2026, 3, 12), PriceResolution.FiveMinutes),
                CancellationToken.None);

            report.TargetHitCount.Should().Be(0);
            report.DataInsufficientCount.Should().Be(1);

            var updated = JsonSerializer.Deserialize<DecisionAuditRecord>(
                await File.ReadAllTextAsync(auditPath),
                DecisionAuditJson.Options);
            updated!.PaperOutcomes.Should().ContainSingle();
            updated.PaperOutcomes[0].Status.Should().Be(PaperTradeOutcomeStatus.DataInsufficient);
            updated.PaperOutcomes[0].Reason.Should().Contain("First missing range");
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    private static DecisionAuditRecord CreateAuditRecord(InstrumentId instrument)
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

        return new DecisionAuditRecord(
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
            [assessment],
            [candidate],
            [],
            [],
            DecisionBiasSummary.From([assessment], [candidate]));
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
