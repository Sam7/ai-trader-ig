using FluentAssertions;
using Trading.Abstractions;
using Trading.Strategy.Agents;
using Trading.Strategy.Configuration;
using Trading.Strategy.Context;
using Trading.Strategy.Execution;
using Trading.Strategy.Monitoring;
using Trading.Strategy.Workflow;

namespace Trading.Strategy.Tests;

public class TradingStrategyDirectorTests
{
    [Fact]
    public async Task PrepareTradingDayAsync_ShouldPersistBriefingWithExactShortlistSize()
    {
        var harness = StrategyHarness.Create();
        var request = new TradingDayRequest(new DateOnly(2026, 03, 11));

        var briefing = await harness.Director.PrepareTradingDayAsync(request);
        var savedState = await harness.StateStore.GetAsync(request.TradingDate);

        briefing.Shortlist.Should().HaveCount(3);
        savedState.Should().NotBeNull();
        savedState!.DailyBriefing.Should().NotBeNull();
        savedState.DailyBriefing!.Shortlist.Should().HaveCount(3);
    }

    [Fact]
    public async Task ReactToMarketEventAsync_WhenInstrumentIsNotShortlisted_ShouldIgnore()
    {
        var harness = StrategyHarness.Create();
        await harness.Director.PrepareTradingDayAsync(new TradingDayRequest(new DateOnly(2026, 03, 11)));

        var reaction = await harness.Director.ReactToMarketEventAsync(new MarketEvent(
            "event-1",
            new InstrumentId("CC.D.BRENT.UMA.IP"),
            MarketEventKind.PriceTick,
            new MarketSnapshot(new InstrumentId("CC.D.BRENT.UMA.IP"), 81m, 80.9m, 81.1m, 1m, 1m, harness.Clock.UtcNow),
            harness.Clock.UtcNow));

        reaction.Kind.Should().Be(MarketReactionKind.Ignored);
    }

    [Fact]
    public async Task ReactToMarketEventAsync_WhenPlannerReturnsNoTrade_ShouldReturnNoTrade()
    {
        var harness = StrategyHarness.Create(plannerResult: TradePlanningResult.FromNoTrade(
            new NoTradeDecision(NoTradeReasonCode.WeakEdge, "No clean setup.", DateTimeOffset.UtcNow)));
        await harness.Director.PrepareTradingDayAsync(new TradingDayRequest(new DateOnly(2026, 03, 11)));

        var reaction = await harness.Director.ReactToMarketEventAsync(harness.CreateEntryZoneEvent("event-2"));

        reaction.Kind.Should().Be(MarketReactionKind.NoTrade);
        reaction.NoTradeDecision!.ReasonCode.Should().Be(NoTradeReasonCode.WeakEdge);
    }

    [Fact]
    public async Task ReactToMarketEventAsync_WhenRiskGateRejects_ShouldReturnProposalRejected()
    {
        var harness = StrategyHarness.Create(riskGateDecision: new RiskGateDecision(false, "Risk gate says no.", DateTimeOffset.UtcNow, NoTradeReasonCode.RiskGateRejected));
        await harness.Director.PrepareTradingDayAsync(new TradingDayRequest(new DateOnly(2026, 03, 11)));

        var reaction = await harness.Director.ReactToMarketEventAsync(harness.CreateEntryZoneEvent("event-3"));

        reaction.Kind.Should().Be(MarketReactionKind.ProposalRejected);
        reaction.RiskGateDecision!.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task ReactToMarketEventAsync_WhenTradeIsApproved_ShouldReturnExecutionIntent()
    {
        var harness = StrategyHarness.Create();
        await harness.Director.PrepareTradingDayAsync(new TradingDayRequest(new DateOnly(2026, 03, 11)));

        var reaction = await harness.Director.ReactToMarketEventAsync(harness.CreateEntryZoneEvent("event-4"));

        reaction.Kind.Should().Be(MarketReactionKind.ExecutionReady);
        reaction.ExecutionIntent.Should().NotBeNull();
        reaction.ExecutionIntent!.Quantity.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task ReactToMarketEventAsync_WhenDailyLimitReached_ShouldRejectInExecutionPolicy()
    {
        var harness = StrategyHarness.Create();
        await harness.Director.PrepareTradingDayAsync(new TradingDayRequest(new DateOnly(2026, 03, 11)));
        await harness.StateStore.SaveAsync(TradingDayState.Empty(new DateOnly(2026, 03, 11)) with
        {
            DailyBriefing = harness.Briefing,
            DailyTradeCount = StrategyProfile.Default.TradeLimitsPolicy.MaxDailyTrades
        });

        var reaction = await harness.Director.ReactToMarketEventAsync(harness.CreateEntryZoneEvent("event-5"));

        reaction.Kind.Should().Be(MarketReactionKind.NoTrade);
        reaction.NoTradeDecision!.ReasonCode.Should().Be(NoTradeReasonCode.DailyLimitReached);
    }

    [Fact]
    public async Task ReactToMarketEventAsync_WhenSpreadIsTooWide_ShouldRejectInExecutionPolicy()
    {
        var harness = StrategyHarness.Create();
        await harness.Director.PrepareTradingDayAsync(new TradingDayRequest(new DateOnly(2026, 03, 11)));

        var reaction = await harness.Director.ReactToMarketEventAsync(harness.CreateWideSpreadEvent("event-5b"));

        reaction.Kind.Should().Be(MarketReactionKind.NoTrade);
        reaction.NoTradeDecision!.ReasonCode.Should().Be(NoTradeReasonCode.SpreadTooWide);
    }

    [Fact]
    public async Task RecordExecutionOutcomeAsync_WhenTradeIsSubmitted_ShouldOpenTradeAndIncrementCount()
    {
        var harness = StrategyHarness.Create();
        await harness.Director.PrepareTradingDayAsync(new TradingDayRequest(new DateOnly(2026, 03, 11)));
        var reaction = await harness.Director.ReactToMarketEventAsync(harness.CreateEntryZoneEvent("event-6"));

        var state = await harness.Director.RecordExecutionOutcomeAsync(new ExecutionOutcome(
            reaction.ExecutionIntent!.Instrument,
            ExecutionLifecycleStatus.Submitted,
            harness.Clock.UtcNow,
            "broker-1",
            reaction.ExecutionIntent.Quantity));

        state.DailyTradeCount.Should().Be(1);
        state.OpenTrade.Should().NotBeNull();
        state.PendingProposal.Should().BeNull();
        state.PendingExecutionIntent.Should().BeNull();
        state.OpenTrade!.ExecutionIntent.RiskAmount.Should().Be(reaction.ExecutionIntent.RiskAmount);
    }

    [Fact]
    public async Task RecordExecutionOutcomeAsync_WithoutPendingExecutionIntent_ShouldFailFast()
    {
        var harness = StrategyHarness.Create();
        await harness.Director.PrepareTradingDayAsync(new TradingDayRequest(new DateOnly(2026, 03, 11)));
        var reaction = await harness.Director.ReactToMarketEventAsync(harness.CreateEntryZoneEvent("event-6b"));
        var pendingState = await harness.StateStore.GetAsync(new DateOnly(2026, 03, 11));

        await harness.StateStore.SaveAsync(pendingState! with
        {
            PendingExecutionIntent = null,
        });

        var action = () => harness.Director.RecordExecutionOutcomeAsync(new ExecutionOutcome(
            reaction.ExecutionIntent!.Instrument,
            ExecutionLifecycleStatus.Submitted,
            harness.Clock.UtcNow,
            "broker-1",
            reaction.ExecutionIntent.Quantity));

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*pending execution intent*");
    }

    [Fact]
    public async Task ReviewOpenTradeAsync_WhenOpenTradeAnomalyOccurs_ShouldRecommendExit()
    {
        var harness = StrategyHarness.Create();
        await harness.Director.PrepareTradingDayAsync(new TradingDayRequest(new DateOnly(2026, 03, 11)));
        var reaction = await harness.Director.ReactToMarketEventAsync(harness.CreateEntryZoneEvent("event-7"));
        await harness.Director.RecordExecutionOutcomeAsync(new ExecutionOutcome(
            reaction.ExecutionIntent!.Instrument,
            ExecutionLifecycleStatus.Filled,
            harness.Clock.UtcNow,
            "broker-2",
            reaction.ExecutionIntent.Quantity));

        var decision = await harness.Director.ReviewOpenTradeAsync(new OpenTradeReviewRequest(StrategyTrigger.OpenTradeAnomaly));

        decision.Action.Should().Be(TradeManagementAction.ExitTrade);
    }

    private sealed class StrategyHarness
    {
        private StrategyHarness(
            FakeResearchBriefingAgent researchAgent,
            FakeTradePlannerAgent plannerAgent,
            FakeRiskGateAgent riskGateAgent,
            FakeMarketSnapshotSource marketSnapshotSource,
            FakeHeadlineSource headlineSource,
            FakeEconomicCalendarSource calendarSource,
            FakeTradingClock clock,
            FakeExposureStateSource exposureStateSource,
            InMemoryTradingDayStateStore stateStore,
            TradingStrategyDirector director)
        {
            ResearchAgent = researchAgent;
            PlannerAgent = plannerAgent;
            RiskGateAgent = riskGateAgent;
            MarketSnapshotSource = marketSnapshotSource;
            HeadlineSource = headlineSource;
            CalendarSource = calendarSource;
            Clock = clock;
            ExposureStateSource = exposureStateSource;
            StateStore = stateStore;
            Director = director;
        }

        public DailyBriefing Briefing => ResearchAgent.Briefing;

        public FakeResearchBriefingAgent ResearchAgent { get; }

        public FakeTradePlannerAgent PlannerAgent { get; }

        public FakeRiskGateAgent RiskGateAgent { get; }

        public FakeMarketSnapshotSource MarketSnapshotSource { get; }

        public FakeHeadlineSource HeadlineSource { get; }

        public FakeEconomicCalendarSource CalendarSource { get; }

        public FakeTradingClock Clock { get; }

        public FakeExposureStateSource ExposureStateSource { get; }

        public InMemoryTradingDayStateStore StateStore { get; }

        public TradingStrategyDirector Director { get; }

        public static StrategyHarness Create(
            TradePlanningResult? plannerResult = null,
            RiskGateDecision? riskGateDecision = null)
        {
            var clock = new FakeTradingClock(DateTimeOffset.Parse("2026-03-11T06:00:00Z"));
            var researchAgent = new FakeResearchBriefingAgent(clock.UtcNow);
            var plannerAgent = new FakeTradePlannerAgent(plannerResult ?? TradePlanningResult.FromProposal(CreateProposal(clock.UtcNow)));
            var gateAgent = new FakeRiskGateAgent(riskGateDecision ?? new RiskGateDecision(true, "Approved.", clock.UtcNow));
            var marketSource = new FakeMarketSnapshotSource(clock.UtcNow);
            var headlineSource = new FakeHeadlineSource();
            var calendarSource = new FakeEconomicCalendarSource();
            var exposureStateSource = new FakeExposureStateSource();
            var stateStore = new InMemoryTradingDayStateStore();
            var director = new TradingStrategyDirector(
                StrategyProfile.Default,
                researchAgent,
                plannerAgent,
                gateAgent,
                marketSource,
                headlineSource,
                calendarSource,
                clock,
                exposureStateSource,
                stateStore,
                new MarketMonitor(),
                new ExecutionPolicy());

            return new StrategyHarness(
                researchAgent,
                plannerAgent,
                gateAgent,
                marketSource,
                headlineSource,
                calendarSource,
                clock,
                exposureStateSource,
                stateStore,
                director);
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

        private static TradeProposal CreateProposal(DateTimeOffset nowUtc)
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

    private sealed class FakeResearchBriefingAgent : IResearchBriefingAgent
    {
        public FakeResearchBriefingAgent(DateTimeOffset nowUtc)
        {
            Briefing = new DailyBriefing(
                new DateOnly(2026, 03, 11),
                "Macro calm with USD in focus.",
                "Mixed but selective.",
                MarketRegime.Mixed,
                [
                    CreateWatchlist("CS.D.EURUSD.CFD.IP", 1, 1.0990m, 1.1010m),
                    CreateWatchlist("CC.D.GOLD.UMA.IP", 2, 2900m, 2910m),
                    CreateWatchlist("CS.D.USDJPY.CFD.IP", 3, 147m, 148m),
                    CreateWatchlist("IX.D.SPTRD.DAILY.IP", 4, 5000m, 5010m),
                ],
                [
                    CreateWatchlist("CS.D.EURUSD.CFD.IP", 1, 1.0990m, 1.1010m),
                    CreateWatchlist("CC.D.GOLD.UMA.IP", 2, 2900m, 2910m),
                    CreateWatchlist("CS.D.USDJPY.CFD.IP", 3, 147m, 148m),
                ],
                [],
                nowUtc);
        }

        public DailyBriefing Briefing { get; }

        public Task<DailyBriefing> CreateDailyBriefingAsync(ResearchBriefingInput input, CancellationToken cancellationToken = default)
            => Task.FromResult(Briefing);

        private static WatchlistEntry CreateWatchlist(string instrument, int rank, decimal lower, decimal upper)
            => new(
                new InstrumentId(instrument),
                rank,
                $"Ranked #{rank}",
                lower,
                upper,
                new TradeHypothesis(TradeDirection.Buy, "Long thesis", "Breakout holds", "Breaks support", ["macro"], null),
                new TradeHypothesis(TradeDirection.Sell, "Short thesis", "Breakdown holds", "Reclaims resistance", ["macro"], null));
    }

    private sealed class FakeTradePlannerAgent : ITradePlannerAgent
    {
        private readonly TradePlanningResult _result;

        public FakeTradePlannerAgent(TradePlanningResult result)
        {
            _result = result;
        }

        public Task<TradePlanningResult> CreateTradePlanAsync(TradePlanningInput input, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class FakeRiskGateAgent : IRiskGateAgent
    {
        private readonly RiskGateDecision _decision;

        public FakeRiskGateAgent(RiskGateDecision decision)
        {
            _decision = decision;
        }

        public Task<RiskGateDecision> EvaluateAsync(RiskGateInput input, CancellationToken cancellationToken = default)
            => Task.FromResult(_decision);
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

    private sealed class FakeHeadlineSource : IHeadlineSource
    {
        public Task<IReadOnlyList<HeadlineItem>> GetHeadlinesAsync(HeadlineQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<HeadlineItem>>([]);
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

    private sealed class FakeExposureStateSource : IExposureStateSource
    {
        public Task<ExposureState> GetExposureStateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ExposureState(100_000m, 5_000m, [], []));
    }
}
