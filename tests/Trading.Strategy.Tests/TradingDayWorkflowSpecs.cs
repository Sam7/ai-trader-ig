using FluentAssertions;
using Trading.Abstractions;
using Trading.Strategy.ActiveTradeManagement;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.ExecutionReporting;
using Trading.Strategy.Inputs;
using Trading.Strategy.MarketAttention;
using Trading.Strategy.OpportunityReview;
using Trading.Strategy.Persistence;
using Trading.Strategy.Rules;
using Trading.Strategy.Shared;
using Trading.Strategy.Workflow;

namespace Trading.Strategy.Tests;

public class TradingDayWorkflowSpecs
{
    [Fact]
    public async Task PlanTradingDayAsync_should_save_a_three_market_plan()
    {
        var harness = WorkflowHarness.Create();

        var plan = await harness.Workflow.PlanTradingDayAsync(new TradingDayRequest(new DateOnly(2026, 03, 11)));
        var record = await harness.Store.GetAsync(new DateOnly(2026, 03, 11));

        plan.WatchList.Should().HaveCount(3);
        record.Should().NotBeNull();
        record!.Plan.Should().NotBeNull();
        record.Plan!.WatchList.Should().HaveCount(3);
    }

    [Fact]
    public async Task AssessMarketAsync_should_ignore_updates_for_unwatched_markets()
    {
        var harness = WorkflowHarness.Create();
        await harness.Workflow.PlanTradingDayAsync(new TradingDayRequest(new DateOnly(2026, 03, 11)));

        var attention = await harness.Workflow.AssessMarketAsync(new MarketEvent(
            "event-1",
            new InstrumentId("CC.D.BRENT.UMA.IP"),
            MarketEventKind.PriceTick,
            new MarketSnapshot(new InstrumentId("CC.D.BRENT.UMA.IP"), 81m, 80.9m, 81.1m, 1m, 1m, harness.Clock.UtcNow),
            harness.Clock.UtcNow));

        attention.Should().BeOfType<IgnoreMarketUpdate>();
    }

    [Fact]
    public async Task AssessMarketAsync_should_raise_review_when_price_enters_the_entry_zone()
    {
        var harness = WorkflowHarness.Create();
        await harness.Workflow.PlanTradingDayAsync(new TradingDayRequest(new DateOnly(2026, 03, 11)));

        var attention = await harness.Workflow.AssessMarketAsync(harness.CreateEntryZoneEvent("event-2"));

        attention.Should().BeOfType<ReviewMarketUpdate>();
    }

    [Fact]
    public async Task ReviewOpportunityAsync_should_stand_aside_when_the_setup_is_weak()
    {
        var harness = WorkflowHarness.Create(planningResult: new StandAsideSetup(
            new StandAsideDecision(StandAsideReason.WeakEdge, "No clean setup.", DateTimeOffset.UtcNow)));
        await harness.Workflow.PlanTradingDayAsync(new TradingDayRequest(new DateOnly(2026, 03, 11)));
        var attention = (ReviewMarketUpdate)await harness.Workflow.AssessMarketAsync(harness.CreateEntryZoneEvent("event-3"));

        var review = await harness.Workflow.ReviewOpportunityAsync(attention);

        review.Should().BeOfType<StandAsideOpportunity>();
    }

    [Fact]
    public async Task ReviewOpportunityAsync_should_approve_a_trade_when_setup_and_risk_pass()
    {
        var harness = WorkflowHarness.Create();
        await harness.Workflow.PlanTradingDayAsync(new TradingDayRequest(new DateOnly(2026, 03, 11)));
        var attention = (ReviewMarketUpdate)await harness.Workflow.AssessMarketAsync(harness.CreateEntryZoneEvent("event-4"));

        var review = await harness.Workflow.ReviewOpportunityAsync(attention);

        review.Should().BeOfType<ApprovedOpportunity>();
        ((ApprovedOpportunity)review).ApprovedTrade.Quantity.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task ReviewOpportunityAsync_should_stand_aside_when_spread_is_too_wide()
    {
        var harness = WorkflowHarness.Create();
        await harness.Workflow.PlanTradingDayAsync(new TradingDayRequest(new DateOnly(2026, 03, 11)));
        var attention = (ReviewMarketUpdate)await harness.Workflow.AssessMarketAsync(harness.CreateWideSpreadEvent("event-5"));

        var review = await harness.Workflow.ReviewOpportunityAsync(attention);

        review.Should().BeOfType<StandAsideOpportunity>();
        ((StandAsideOpportunity)review).Decision.Reason.Should().Be(StandAsideReason.SpreadTooWide);
    }

    [Fact]
    public async Task ApplyExecutionReportAsync_should_promote_an_approved_trade_to_an_active_trade()
    {
        var harness = WorkflowHarness.Create();
        await harness.Workflow.PlanTradingDayAsync(new TradingDayRequest(new DateOnly(2026, 03, 11)));
        var attention = (ReviewMarketUpdate)await harness.Workflow.AssessMarketAsync(harness.CreateEntryZoneEvent("event-6"));
        var review = (ApprovedOpportunity)await harness.Workflow.ReviewOpportunityAsync(attention);

        var status = await harness.Workflow.ApplyExecutionReportAsync(new ExecutionReport(
            review.ApprovedTrade.Instrument,
            ExecutionReportType.Submitted,
            harness.Clock.UtcNow,
            "broker-1",
            review.ApprovedTrade.Quantity));

        status.ExecutedTradeCount.Should().Be(1);
        status.PendingTrade.Should().BeNull();
        status.ActiveTrade.Should().NotBeNull();
    }

    [Fact]
    public async Task ReviewActiveTradeAsync_should_recommend_exit_on_execution_anomaly()
    {
        var harness = WorkflowHarness.Create();
        await harness.Workflow.PlanTradingDayAsync(new TradingDayRequest(new DateOnly(2026, 03, 11)));
        var attention = (ReviewMarketUpdate)await harness.Workflow.AssessMarketAsync(harness.CreateEntryZoneEvent("event-7"));
        var review = (ApprovedOpportunity)await harness.Workflow.ReviewOpportunityAsync(attention);
        await harness.Workflow.ApplyExecutionReportAsync(new ExecutionReport(
            review.ApprovedTrade.Instrument,
            ExecutionReportType.Filled,
            harness.Clock.UtcNow,
            "broker-2",
            review.ApprovedTrade.Quantity));

        var decision = await harness.Workflow.ReviewActiveTradeAsync(new ActiveTradeReviewRequest(MarketSignal.OpenTradeAnomaly));

        decision.Should().BeOfType<ExitTrade>();
    }

    private sealed class WorkflowHarness
    {
        private WorkflowHarness(
            FakeDailyBriefingComposer composer,
            FakeTradeSetupPlanner setupPlanner,
            FakeTradeApprover tradeApprover,
            FakeMarketSnapshotSource marketSnapshotSource,
            FakeNewsHeadlineSource newsHeadlineSource,
            FakeEconomicCalendarSource economicCalendarSource,
            FakeTradingClock clock,
            FakeRiskContextSource riskContextSource,
            InMemoryTradingDayStore store,
            ITradingDayWorkflow workflow)
        {
            Composer = composer;
            SetupPlanner = setupPlanner;
            TradeApprover = tradeApprover;
            MarketSnapshotSource = marketSnapshotSource;
            NewsHeadlineSource = newsHeadlineSource;
            EconomicCalendarSource = economicCalendarSource;
            Clock = clock;
            RiskContextSource = riskContextSource;
            Store = store;
            Workflow = workflow;
        }

        public FakeDailyBriefingComposer Composer { get; }

        public FakeTradeSetupPlanner SetupPlanner { get; }

        public FakeTradeApprover TradeApprover { get; }

        public FakeMarketSnapshotSource MarketSnapshotSource { get; }

        public FakeNewsHeadlineSource NewsHeadlineSource { get; }

        public FakeEconomicCalendarSource EconomicCalendarSource { get; }

        public FakeTradingClock Clock { get; }

        public FakeRiskContextSource RiskContextSource { get; }

        public InMemoryTradingDayStore Store { get; }

        public ITradingDayWorkflow Workflow { get; }

        public static WorkflowHarness Create(TradeSetupPlanningResult? planningResult = null)
        {
            var clock = new FakeTradingClock(DateTimeOffset.Parse("2026-03-11T06:00:00Z"));
            var composer = new FakeDailyBriefingComposer(clock.UtcNow);
            var setupPlanner = new FakeTradeSetupPlanner(planningResult ?? new PlannedTradeSetup(CreateTradeSetup(clock.UtcNow)));
            var tradeApprover = new FakeTradeApprover(new TradeApproval("Approved.", clock.UtcNow));
            var marketSnapshotSource = new FakeMarketSnapshotSource(clock.UtcNow);
            var newsHeadlineSource = new FakeNewsHeadlineSource();
            var economicCalendarSource = new FakeEconomicCalendarSource();
            var riskContextSource = new FakeRiskContextSource();
            var store = new InMemoryTradingDayStore();
            var rules = StrategyRules.Default;
            var workflow = new TradingDayWorkflow(
                new TradingDayPlanner(rules, composer, clock, store),
                new MarketAttentionService(rules, store),
                new OpportunityReviewer(riskContextSource, setupPlanner, tradeApprover, store, clock, new PositionSizer()),
                new ActiveTradeReviewer(clock, store, rules, new BreakEvenStopRule()),
                new ExecutionReportApplier(store));

            return new WorkflowHarness(
                composer,
                setupPlanner,
                tradeApprover,
                marketSnapshotSource,
                newsHeadlineSource,
                economicCalendarSource,
                clock,
                riskContextSource,
                store,
                workflow);
        }

        public MarketEvent CreateEntryZoneEvent(string eventId)
            => new(
                eventId,
                new InstrumentId("CS.D.EURUSD.CFD.IP"),
                MarketEventKind.PriceTick,
                new MarketSnapshot(new InstrumentId("CS.D.EURUSD.CFD.IP"), 1.1000m, 1.0999m, 1.1001m, 0.002m, 1.2m, Clock.UtcNow),
                Clock.UtcNow);

        public MarketEvent CreateWideSpreadEvent(string eventId)
            => new(
                eventId,
                new InstrumentId("CS.D.EURUSD.CFD.IP"),
                MarketEventKind.PriceTick,
                new MarketSnapshot(new InstrumentId("CS.D.EURUSD.CFD.IP"), 1.1000m, 0.7900m, 1.4100m, 0.002m, 1.2m, Clock.UtcNow),
                Clock.UtcNow);

        private static TradeSetup CreateTradeSetup(DateTimeOffset nowUtc)
            => new(
                new InstrumentId("CS.D.EURUSD.CFD.IP"),
                TradeDirection.Buy,
                1.1000m,
                1.0950m,
                1.1100m,
                0.82m,
                "Macro divergence",
                "Buy the pullback",
                "Breaks support",
                nowUtc);
    }

    private sealed class FakeDailyBriefingComposer : IDailyBriefingComposer
    {
        public FakeDailyBriefingComposer(DateTimeOffset nowUtc)
        {
            Plan = new TradingDayPlan(
                new DateOnly(2026, 03, 11),
                "Macro calm with USD in focus.",
                "Mixed but selective.",
                MarketRegime.Mixed,
                [
                    CreateMarketWatch("CS.D.EURUSD.CFD.IP", 1, 1.0990m, 1.1010m),
                    CreateMarketWatch("CC.D.GOLD.UMA.IP", 2, 2900m, 2910m),
                    CreateMarketWatch("CS.D.USDJPY.CFD.IP", 3, 147m, 148m),
                    CreateMarketWatch("IX.D.SPTRD.DAILY.IP", 4, 5000m, 5010m),
                ],
                [
                    CreateMarketWatch("CS.D.EURUSD.CFD.IP", 1, 1.0990m, 1.1010m),
                    CreateMarketWatch("CC.D.GOLD.UMA.IP", 2, 2900m, 2910m),
                    CreateMarketWatch("CS.D.USDJPY.CFD.IP", 3, 147m, 148m),
                ],
                [],
                nowUtc);
        }

        public TradingDayPlan Plan { get; }

        public Task<TradingDayPlan> ComposeAsync(DailyBriefingRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Plan);

        private static MarketWatch CreateMarketWatch(string instrument, int rank, decimal lower, decimal upper)
            => new(
                new InstrumentId(instrument),
                rank,
                $"Ranked #{rank}",
                lower,
                upper,
                new TradeScenario(TradeDirection.Buy, "Long thesis", "Breakout holds", "Breaks support", ["macro"], null),
                new TradeScenario(TradeDirection.Sell, "Short thesis", "Breakdown holds", "Reclaims resistance", ["macro"], null));
    }

    private sealed class FakeTradeSetupPlanner : ITradeSetupPlanner
    {
        private readonly TradeSetupPlanningResult _result;

        public FakeTradeSetupPlanner(TradeSetupPlanningResult result)
        {
            _result = result;
        }

        public Task<TradeSetupPlanningResult> PlanAsync(PendingOpportunityReview review, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class FakeTradeApprover : ITradeApprover
    {
        private readonly TradeApproval? _approval;

        public FakeTradeApprover(TradeApproval? approval)
        {
            _approval = approval;
        }

        public Task<TradeApproval?> ApproveAsync(PendingOpportunityReview review, TradeSetup tradeSetup, CancellationToken cancellationToken = default)
            => Task.FromResult(_approval);
    }

    private sealed class FakeMarketSnapshotSource : IMarketSnapshotSource
    {
        private readonly DateTimeOffset _nowUtc;

        public FakeMarketSnapshotSource(DateTimeOffset nowUtc)
        {
            _nowUtc = nowUtc;
        }

        public Task<MarketUniverseSnapshot> GetUniverseSnapshotAsync(DateOnly tradingDate, CancellationToken cancellationToken = default)
            => Task.FromResult(new MarketUniverseSnapshot(
                tradingDate,
                [
                    new MarketSnapshot(new InstrumentId("CS.D.EURUSD.CFD.IP"), 1.1m, 1.0999m, 1.1001m, 0.002m, 1.2m, _nowUtc),
                    new MarketSnapshot(new InstrumentId("CC.D.GOLD.UMA.IP"), 2905m, 2904m, 2906m, 10m, 1.1m, _nowUtc),
                    new MarketSnapshot(new InstrumentId("CS.D.USDJPY.CFD.IP"), 147.5m, 147.4m, 147.6m, 0.5m, 1.0m, _nowUtc),
                ]));

        public Task<MarketSnapshot?> GetSnapshotAsync(InstrumentId instrument, CancellationToken cancellationToken = default)
            => Task.FromResult<MarketSnapshot?>(new MarketSnapshot(instrument, 1.1m, 1.0999m, 1.1001m, 0.002m, 1.2m, _nowUtc));
    }

    private sealed class FakeNewsHeadlineSource : INewsHeadlineSource
    {
        public Task<IReadOnlyList<NewsHeadline>> GetHeadlinesAsync(HeadlineQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<NewsHeadline>>([]);
    }

    private sealed class FakeEconomicCalendarSource : IEconomicCalendarSource
    {
        public Task<IReadOnlyList<EconomicEvent>> GetEventsAsync(CalendarWindow window, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EconomicEvent>>([]);
    }

    private sealed class FakeTradingClock : ITradingClock
    {
        public FakeTradingClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FakeRiskContextSource : IRiskContextSource
    {
        public Task<RiskContext> GetRiskContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new RiskContext(100_000m, 5_000m, [], []));
    }
}
