using Trading.Abstractions;
using Trading.Strategy.Agents;
using Trading.Strategy.Configuration;
using Trading.Strategy.Context;
using Trading.Strategy.Execution;
using Trading.Strategy.Monitoring;

namespace Trading.Strategy.Workflow;

public sealed class TradingStrategyDirector : ITradingStrategyDirector
{
    private readonly StrategyProfile _profile;
    private readonly IResearchBriefingAgent _researchBriefingAgent;
    private readonly ITradePlannerAgent _tradePlannerAgent;
    private readonly IRiskGateAgent _riskGateAgent;
    private readonly IMarketSnapshotSource _marketSnapshotSource;
    private readonly IHeadlineSource _headlineSource;
    private readonly IEconomicCalendarSource _economicCalendarSource;
    private readonly ITradingClock _tradingClock;
    private readonly IExposureStateSource _exposureStateSource;
    private readonly ITradingDayStateStore _stateStore;
    private readonly MarketMonitor _marketMonitor;
    private readonly ExecutionPolicy _executionPolicy;

    public TradingStrategyDirector(
        StrategyProfile profile,
        IResearchBriefingAgent researchBriefingAgent,
        ITradePlannerAgent tradePlannerAgent,
        IRiskGateAgent riskGateAgent,
        IMarketSnapshotSource marketSnapshotSource,
        IHeadlineSource headlineSource,
        IEconomicCalendarSource economicCalendarSource,
        ITradingClock tradingClock,
        IExposureStateSource exposureStateSource,
        ITradingDayStateStore stateStore,
        MarketMonitor marketMonitor,
        ExecutionPolicy executionPolicy)
    {
        _profile = profile;
        _researchBriefingAgent = researchBriefingAgent;
        _tradePlannerAgent = tradePlannerAgent;
        _riskGateAgent = riskGateAgent;
        _marketSnapshotSource = marketSnapshotSource;
        _headlineSource = headlineSource;
        _economicCalendarSource = economicCalendarSource;
        _tradingClock = tradingClock;
        _exposureStateSource = exposureStateSource;
        _stateStore = stateStore;
        _marketMonitor = marketMonitor;
        _executionPolicy = executionPolicy;
    }

    public async Task<DailyBriefing> PrepareTradingDayAsync(TradingDayRequest request, CancellationToken cancellationToken = default)
    {
        _profile.Validate();

        var nowUtc = _tradingClock.UtcNow;
        var marketUniverse = await _marketSnapshotSource.GetUniverseSnapshotAsync(request.TradingDate, cancellationToken);
        var headlines = await _headlineSource.GetHeadlinesAsync(new HeadlineQuery([], nowUtc.AddHours(-24), nowUtc), cancellationToken);
        var events = await _economicCalendarSource.GetEventsAsync(new CalendarWindow(nowUtc, nowUtc.AddHours(24)), cancellationToken);

        var briefing = await _researchBriefingAgent.CreateDailyBriefingAsync(
            new ResearchBriefingInput(request, _profile, marketUniverse, headlines, events, nowUtc),
            cancellationToken);

        briefing.Validate(_profile.MonitoringPolicy.ShortlistSize);

        var state = TradingDayState.Empty(request.TradingDate) with { DailyBriefing = briefing };
        await _stateStore.SaveAsync(state, cancellationToken);
        return briefing;
    }

    public async Task<MarketReaction> ReactToMarketEventAsync(MarketEvent marketEvent, CancellationToken cancellationToken = default)
    {
        var tradingDate = DateOnly.FromDateTime(marketEvent.OccurredAtUtc.UtcDateTime);
        var state = await GetOrCreateStateAsync(tradingDate, cancellationToken);
        if (state.DailyBriefing is null)
        {
            return new MarketReaction(MarketReactionKind.Ignored, "Trading day has not been prepared.", StrategyTrigger.None);
        }

        if (state.ConsumedTriggers.Any(x => string.Equals(x.EventId, marketEvent.EventId, StringComparison.Ordinal)))
        {
            return new MarketReaction(MarketReactionKind.Ignored, "Event was already consumed.", StrategyTrigger.None);
        }

        var watchlistEntry = state.DailyBriefing.Shortlist.FirstOrDefault(x => x.Instrument == marketEvent.Instrument);
        if (watchlistEntry is null)
        {
            return new MarketReaction(MarketReactionKind.Ignored, "Instrument is not on the shortlist.", StrategyTrigger.None);
        }

        var trigger = _marketMonitor.DetectTrigger(watchlistEntry, marketEvent, state.OpenTrade, _profile.MonitoringPolicy);
        if (trigger == StrategyTrigger.None)
        {
            return await PersistReactionAsync(
                state,
                marketEvent,
                trigger,
                new MarketReaction(MarketReactionKind.Ignored, "No meaningful change detected.", StrategyTrigger.None),
                cancellationToken);
        }

        var recentHeadlines = await _headlineSource.GetHeadlinesAsync(
            new HeadlineQuery([marketEvent.Instrument], marketEvent.OccurredAtUtc.AddHours(-6), marketEvent.OccurredAtUtc),
            cancellationToken);

        var planningResult = await _tradePlannerAgent.CreateTradePlanAsync(
            new TradePlanningInput(_profile, watchlistEntry, marketEvent, recentHeadlines, state.OpenTrade, _tradingClock.UtcNow),
            cancellationToken);

        if (planningResult.NoTradeDecision is not null)
        {
            return await PersistReactionAsync(
                state with { PendingProposal = null, PendingExecutionIntent = null },
                marketEvent,
                trigger,
                new MarketReaction(
                    MarketReactionKind.NoTrade,
                    planningResult.NoTradeDecision.Summary,
                    trigger,
                    NoTradeDecision: planningResult.NoTradeDecision),
                cancellationToken);
        }

        var proposal = planningResult.Proposal ?? throw new InvalidOperationException("Planner returned neither a proposal nor a no-trade decision.");
        proposal.Validate();

        var exposureState = await _exposureStateSource.GetExposureStateAsync(cancellationToken);
        var riskGateDecision = await _riskGateAgent.EvaluateAsync(
            new RiskGateInput(_profile, state, exposureState, proposal, trigger, _tradingClock.UtcNow),
            cancellationToken);

        if (!riskGateDecision.IsApproved)
        {
            return await PersistReactionAsync(
                state with { PendingProposal = null, PendingExecutionIntent = null },
                marketEvent,
                trigger,
                new MarketReaction(
                    MarketReactionKind.ProposalRejected,
                    riskGateDecision.Summary,
                    trigger,
                    Proposal: proposal,
                    RiskGateDecision: riskGateDecision,
                    NoTradeDecision: new NoTradeDecision(
                        riskGateDecision.RejectionCode == NoTradeReasonCode.None ? NoTradeReasonCode.RiskGateRejected : riskGateDecision.RejectionCode,
                        riskGateDecision.Summary,
                        riskGateDecision.EvaluatedAtUtc)),
                cancellationToken);
        }

        var executionDecision = _executionPolicy.BuildIntent(
            _profile,
            state,
            exposureState,
            proposal,
            marketEvent.Snapshot,
            state.DailyBriefing.CalendarEvents,
            _tradingClock.UtcNow);

        if (executionDecision.NoTradeDecision is not null)
        {
            return await PersistReactionAsync(
                state with { PendingProposal = null, PendingExecutionIntent = null },
                marketEvent,
                trigger,
                new MarketReaction(
                    MarketReactionKind.NoTrade,
                    executionDecision.NoTradeDecision.Summary,
                    trigger,
                    Proposal: proposal,
                    RiskGateDecision: riskGateDecision,
                    NoTradeDecision: executionDecision.NoTradeDecision),
                cancellationToken);
        }

        var executionIntent = executionDecision.ExecutionIntent ?? throw new InvalidOperationException("Execution policy approved without creating an intent.");
        return await PersistReactionAsync(
            state with { PendingProposal = proposal, PendingExecutionIntent = executionIntent },
            marketEvent,
            trigger,
            new MarketReaction(
                MarketReactionKind.ExecutionReady,
                "Execution intent is ready.",
                trigger,
                Proposal: proposal,
                RiskGateDecision: riskGateDecision,
                ExecutionIntent: executionIntent),
            cancellationToken);
    }

    public async Task<TradeManagementDecision> ReviewOpenTradeAsync(OpenTradeReviewRequest request, CancellationToken cancellationToken = default)
    {
        var state = await GetOrCreateStateAsync(DateOnly.FromDateTime(_tradingClock.UtcNow.UtcDateTime), cancellationToken);
        if (state.OpenTrade is null)
        {
            return new TradeManagementDecision(TradeManagementAction.NoOpenTrade, "No open trade exists.", _tradingClock.UtcNow);
        }

        if (request.Instrument is not null && request.Instrument != state.OpenTrade.ExecutionIntent.Instrument)
        {
            return new TradeManagementDecision(TradeManagementAction.NoOpenTrade, "Requested instrument does not match the active trade.", _tradingClock.UtcNow);
        }

        return request.Trigger switch
        {
            StrategyTrigger.OpenTradeAnomaly => new TradeManagementDecision(TradeManagementAction.ExitTrade, "Execution anomaly detected. Exit is preferred.", _tradingClock.UtcNow),
            StrategyTrigger.FreshHeadline or StrategyTrigger.ScheduledEventReleased or StrategyTrigger.VolatilityExpanded
                => new TradeManagementDecision(TradeManagementAction.Escalate, "Meaningful new information warrants a focused review.", _tradingClock.UtcNow),
            StrategyTrigger.EntryZoneTouched when _profile.RiskPolicy.MoveStopToBreakEvenOnHalfTarget
                && request.Snapshot is not null
                && HasReachedHalfTarget(state.OpenTrade.ExecutionIntent, request.Snapshot.LastPrice)
                => new TradeManagementDecision(TradeManagementAction.TightenRisk, "Trade has moved far enough to consider tightening risk.", _tradingClock.UtcNow, state.OpenTrade.ExecutionIntent.EntryPrice),
            _ => new TradeManagementDecision(TradeManagementAction.Hold, "Current management remains mechanical.", _tradingClock.UtcNow),
        };
    }

    public async Task<TradingDayState> RecordExecutionOutcomeAsync(ExecutionOutcome outcome, CancellationToken cancellationToken = default)
    {
        var state = await GetOrCreateStateAsync(DateOnly.FromDateTime(outcome.OccurredAtUtc.UtcDateTime), cancellationToken);
        var nextTradeCount = state.DailyTradeCount;
        var nextPendingProposal = state.PendingProposal;
        var nextPendingExecutionIntent = state.PendingExecutionIntent;
        var nextOpenTrade = state.OpenTrade;

        if (state.PendingProposal is not null && state.PendingProposal.Instrument == outcome.Instrument)
        {
            if (outcome.Status is ExecutionLifecycleStatus.Submitted or ExecutionLifecycleStatus.Filled or ExecutionLifecycleStatus.PartiallyFilled)
            {
                if (state.OpenTrade is null)
                {
                    nextTradeCount++;
                }

                nextOpenTrade = CreateOpenTradeState(state.PendingProposal, state.PendingExecutionIntent, outcome);
                nextPendingProposal = null;
                nextPendingExecutionIntent = null;
            }
            else if (outcome.Status == ExecutionLifecycleStatus.Rejected)
            {
                nextPendingProposal = null;
                nextPendingExecutionIntent = null;
            }
        }
        else if (state.OpenTrade is not null && state.OpenTrade.ExecutionIntent.Instrument == outcome.Instrument)
        {
            nextOpenTrade = outcome.Status is ExecutionLifecycleStatus.StoppedOut or ExecutionLifecycleStatus.TargetHit or ExecutionLifecycleStatus.Closed
                ? null
                : state.OpenTrade with
                {
                    Status = outcome.Status,
                    UpdatedAtUtc = outcome.OccurredAtUtc,
                    BrokerReference = outcome.BrokerReference ?? state.OpenTrade.BrokerReference,
                    FilledQuantity = outcome.FilledQuantity ?? state.OpenTrade.FilledQuantity,
                };
        }

        var nextState = state with
        {
            DailyTradeCount = nextTradeCount,
            PendingProposal = nextPendingProposal,
            PendingExecutionIntent = nextPendingExecutionIntent,
            OpenTrade = nextOpenTrade,
        };

        await _stateStore.SaveAsync(nextState, cancellationToken);
        return nextState;
    }

    private async Task<TradingDayState> GetOrCreateStateAsync(DateOnly tradingDate, CancellationToken cancellationToken)
        => await _stateStore.GetAsync(tradingDate, cancellationToken) ?? TradingDayState.Empty(tradingDate);

    private async Task<MarketReaction> PersistReactionAsync(
        TradingDayState state,
        MarketEvent marketEvent,
        StrategyTrigger trigger,
        MarketReaction reaction,
        CancellationToken cancellationToken)
    {
        var consumedTriggers = state.ConsumedTriggers
            .Append(new ConsumedTrigger(marketEvent.EventId, marketEvent.Instrument, trigger, _tradingClock.UtcNow))
            .ToList();

        await _stateStore.SaveAsync(state with { ConsumedTriggers = consumedTriggers }, cancellationToken);
        return reaction;
    }

    private static OpenTradeState CreateOpenTradeState(
        TradeProposal proposal,
        ExecutionIntent? pendingExecutionIntent,
        ExecutionOutcome outcome)
    {
        if (pendingExecutionIntent is null)
        {
            throw new InvalidOperationException("Cannot open a trade without a pending execution intent.");
        }

        return new OpenTradeState(
            new ExecutionIntent(
                proposal.Instrument,
                proposal.Direction,
                outcome.FilledQuantity ?? pendingExecutionIntent.Quantity,
                proposal.EntryPrice,
                proposal.StopPrice,
                proposal.TargetPrice,
                pendingExecutionIntent.RiskAmount,
                pendingExecutionIntent.RewardRiskRatio,
                pendingExecutionIntent.CreatedAtUtc),
            outcome.Status,
            outcome.OccurredAtUtc,
            outcome.BrokerReference,
            outcome.FilledQuantity);
    }

    private static bool HasReachedHalfTarget(ExecutionIntent executionIntent, decimal currentPrice)
    {
        var halfTarget = executionIntent.Direction == TradeDirection.Buy
            ? executionIntent.EntryPrice + ((executionIntent.TargetPrice - executionIntent.EntryPrice) / 2m)
            : executionIntent.EntryPrice - ((executionIntent.EntryPrice - executionIntent.TargetPrice) / 2m);

        return executionIntent.Direction == TradeDirection.Buy
            ? currentPrice >= halfTarget
            : currentPrice <= halfTarget;
    }
}
