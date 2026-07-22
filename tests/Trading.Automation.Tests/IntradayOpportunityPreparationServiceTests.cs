using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.Abstractions;
using Trading.AI.Configuration;
using Trading.AI.DailyBriefing;
using Trading.AI.Prompts;
using Trading.Automation.Configuration;
using Trading.Automation.Execution;
using Trading.Automation.Health;
using Trading.Charting;
using Trading.Strategy.Inputs;
using Trading.Strategy.Persistence;
using Trading.Strategy.Shared;

public sealed class IntradayOpportunityPreparationServiceTests
{
    [Fact]
    public async Task PrepareAsync_should_compose_a_typed_run_through_replaceable_boundaries()
    {
        var tradingDate = new DateOnly(2026, 7, 3);
        var requestedAtUtc = DateTimeOffset.Parse("2026-07-03T01:00:00Z");
        var instrument = new InstrumentId("CC.D.WTI.UMA.IP");
        var market = new MarketWatch(
            instrument,
            1,
            "Daily rationale",
            new TradeScenario(TradeDirection.Buy, "Long thesis", "Confirm", "Invalidate", [], null),
            new TradeScenario(TradeDirection.Sell, "Short thesis", "Confirm", "Invalidate", [], null));
        var plan = new TradingDayPlan(
            tradingDate,
            "Macro summary",
            "Regime summary",
            MarketRegime.Mixed,
            [market],
            [market],
            [],
            requestedAtUtc.AddHours(-1));
        var tradingDayStore = new InMemoryTradingDayStore();
        await tradingDayStore.SaveAsync(TradingDayRecord.StartNew(plan));
        var priceSource = new FakePriceSeriesSource(new CachedPriceSeriesResult(
            new PriceSeries(
                instrument,
                PriceResolution.TenMinutes,
                [new PriceBar(
                    requestedAtUtc.AddMinutes(-25),
                    80m,
                    81m,
                    79m,
                    80.0m,
                    80.2m,
                    81.2m,
                    79.2m,
                    80.2m,
                    100)]),
            PriceSeriesRefreshMode.LocalCache,
            0));
        var preparationStore = new FakePreparationStore();
        var analysis = new FakeAnalysisService();
        var operationMetrics = new WorkerOperationMetrics();
        var service = new IntradayOpportunityPreparationService(
            tradingDayStore,
            priceSource,
            new FakeChartRenderer([1, 2, 3]),
            analysis,
            preparationStore,
            Options.Create(new AutomationOptions
            {
                IntradayOpportunities = new IntradayOpportunityScanOptions
                {
                    AllowStalePriceDataForDiagnostics = true,
                },
            }),
            Options.Create(new DailyBriefingOptions
            {
                TrackedMarkets =
                [
                    new TrackedMarketOptions
                    {
                        InstrumentId = instrument.Value,
                        DisplayName = "WTI Crude Oil",
                    },
                ],
            }),
            operationMetrics,
            NullLogger<IntradayOpportunityPreparationService>.Instance);

        var document = await service.PrepareAsync(tradingDate, requestedAtUtc);

        document.Should().NotBeNull();
        preparationStore.CapturedRun.Should().NotBeNull();
        preparationStore.CapturedRun!.PromptContract.Should().Be(analysis.Contract);
        preparationStore.CapturedRun.Request.Markets.Should().ContainSingle()
            .Which.InstrumentName.Should().Be("WTI Crude Oil");
        preparationStore.CapturedRun.Markets.Should().ContainSingle();
        preparationStore.CapturedRun.Markets[0].Evidence.Should().ContainSingle();
        preparationStore.CapturedRun.Markets[0].Evidence[0].RecipeId.Should().Be("price-chart-ohlc-compressed");
        priceSource.RequestedInstrument.Should().Be(instrument);
        operationMetrics.Snapshot().RecentCheckpoints.Should().Contain(checkpoint =>
            checkpoint.Operation == "intraday-chart-render"
            && checkpoint.Outcome == WorkerOperationOutcome.Completed
            && checkpoint.PayloadBytes == 3);
    }

    private sealed class FakePriceSeriesSource(CachedPriceSeriesResult result) : IIntradayPriceSeriesSource
    {
        public InstrumentId? RequestedInstrument { get; private set; }

        public Task<CachedPriceSeriesResult> GetSeriesAsync(
            InstrumentId instrument,
            DateTimeOffset requestedAtUtc,
            int chartLookbackHours,
            PriceResolution resolution,
            CancellationToken cancellationToken = default)
        {
            RequestedInstrument = instrument;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeChartRenderer(byte[] data) : IPriceChartRenderer
    {
        public byte[] RenderPng(
            PriceSeries series,
            PriceChartStyle style = PriceChartStyle.Candlestick,
            PriceGapMode gapMode = PriceGapMode.Compress,
            IReadOnlyList<int>? simpleMovingAverageWindows = null,
            int? bollingerPeriod = null,
            int width = 1200,
            int height = 800)
            => data;
    }

    private sealed class FakeAnalysisService : IIntradayOpportunityAnalysisService
    {
        public PromptContractProvenance Contract { get; } = new(
            "intraday-opportunity-review",
            "1",
            new string('a', 64),
            "1",
            new string('b', 64));

        public string RenderRequestText(IntradayOpportunityReviewRequest request) => "rendered request";

        public Task<IntradayOpportunityReviewExecution> AnalyzeAsync(
            IntradayOpportunityPreparationDocument prepared,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakePreparationStore : IIntradayOpportunityPreparationStore
    {
        public IntradayPreparedRun? CapturedRun { get; private set; }

        public Task<IntradayOpportunityPreparationDocument> WriteAsync(
            DateOnly tradingDate,
            DateTimeOffset requestedAtUtc,
            IntradayPreparedRun preparedRun,
            CancellationToken cancellationToken = default)
        {
            CapturedRun = preparedRun;
            var path = Path.GetFullPath("prepared.json");
            var artifact = new ArtifactReference(path, new Uri(path).AbsoluteUri);
            return Task.FromResult(new IntradayOpportunityPreparationDocument(
                tradingDate,
                requestedAtUtc,
                preparedRun.PromptContract.PromptId,
                preparedRun.Request,
                preparedRun.RequestText,
                [],
                [],
                artifact,
                artifact)
            {
                PromptContract = preparedRun.PromptContract,
                PreparationProfile = preparedRun.PreparationProfile,
            });
        }

        public Task<IntradayOpportunityPreparationDocument> LoadAsync(
            string path,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
