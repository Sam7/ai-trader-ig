using Trading.Abstractions;
using Trading.Strategy.Inputs;
using Trading.Strategy.MarketAttention;
using Trading.Strategy.Persistence;
using Trading.Strategy.Rules;
using Trading.Strategy.Shared;

namespace Trading.Strategy.OpportunityReview;

/// <summary>
/// Turns a pending market review into a decision.
/// This is the deliberate step where the strategy moves from "this update matters" to either "stand aside" or "here is the trade to execute."
/// </summary>
public sealed class OpportunityReviewer
{
    private readonly IRiskContextSource _riskContextSource;
    private readonly ITradeSetupPlanner _tradeSetupPlanner;
    private readonly ITradeApprover _tradeApprover;
    private readonly ITradingDayStore _tradingDayStore;
    private readonly ITradingClock _tradingClock;
    private readonly PositionSizer _positionSizer;

    public OpportunityReviewer(
        IRiskContextSource riskContextSource,
        ITradeSetupPlanner tradeSetupPlanner,
        ITradeApprover tradeApprover,
        ITradingDayStore tradingDayStore,
        ITradingClock tradingClock,
        PositionSizer positionSizer)
    {
        _riskContextSource = riskContextSource;
        _tradeSetupPlanner = tradeSetupPlanner;
        _tradeApprover = tradeApprover;
        _tradingDayStore = tradingDayStore;
        _tradingClock = tradingClock;
        _positionSizer = positionSizer;
    }

    public async Task<OpportunityReviewResult> ReviewAsync(ReviewMarketUpdate review, CancellationToken cancellationToken = default)
    {
        var tradingDate = DateOnly.FromDateTime(review.ObservedAtUtc.UtcDateTime);
        var record = await _tradingDayStore.GetAsync(tradingDate, cancellationToken)
            ?? throw new InvalidOperationException("Trading day has not been planned.");

        var pendingReview = record.PendingReview;
        if (pendingReview is null || !string.Equals(pendingReview.ReviewId, review.ReviewId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("No matching pending market review exists.");
        }

        // First ask for the setup itself. If there is no credible setup, stop here.
        var planningResult = await _tradeSetupPlanner.PlanAsync(pendingReview, cancellationToken);
        if (planningResult is StandAsideSetup standAsideSetup)
        {
            await _tradingDayStore.SaveAsync(record.WithStandAside(pendingReview.MarketEvent.EventId), cancellationToken);
            return new StandAsideOpportunity(review.Instrument, standAsideSetup.Decision, _tradingClock.UtcNow);
        }

        var tradeSetup = ((PlannedTradeSetup)planningResult).TradeSetup;
        tradeSetup.Validate();

        // Guard checks are the hard "do not trade" rules that should win before any approval or sizing logic.
        var riskContext = await _riskContextSource.GetRiskContextAsync(cancellationToken);
        var standAsideDecision = EvaluateTradeGuards(record, pendingReview, riskContext, tradeSetup, review.ObservedAtUtc);
        if (standAsideDecision is not null)
        {
            await _tradingDayStore.SaveAsync(record.WithStandAside(pendingReview.MarketEvent.EventId), cancellationToken);
            return new StandAsideOpportunity(review.Instrument, standAsideDecision, _tradingClock.UtcNow);
        }

        // Approval is intentionally later than guard checks: it judges a viable setup, not raw noise.
        var approval = await _tradeApprover.ApproveAsync(pendingReview, tradeSetup, cancellationToken);
        if (approval is null)
        {
            var decision = new StandAsideDecision(StandAsideReason.ApprovalRejected, "Trade approval was withheld.", _tradingClock.UtcNow);
            await _tradingDayStore.SaveAsync(record.WithStandAside(pendingReview.MarketEvent.EventId), cancellationToken);
            return new StandAsideOpportunity(review.Instrument, decision, _tradingClock.UtcNow);
        }

        // Only after the setup passes both guards and approval do we attach size and execution-ready risk numbers.
        var approvedTrade = TryCreateApprovedTrade(tradeSetup, riskContext, pendingReview.Rules, approval);
        if (approvedTrade is null)
        {
            var decision = new StandAsideDecision(StandAsideReason.NoSetup, "Computed trade size rounded down to zero.", _tradingClock.UtcNow);
            await _tradingDayStore.SaveAsync(record.WithStandAside(pendingReview.MarketEvent.EventId), cancellationToken);
            return new StandAsideOpportunity(review.Instrument, decision, _tradingClock.UtcNow);
        }

        await _tradingDayStore.SaveAsync(record.WithPendingTrade(pendingReview.MarketEvent.EventId, approvedTrade), cancellationToken);
        return new ApprovedOpportunity(review.Instrument, tradeSetup, approval, approvedTrade, _tradingClock.UtcNow);
    }

    private StandAsideDecision? EvaluateTradeGuards(
        TradingDayRecord record,
        PendingOpportunityReview pendingReview,
        RiskContext riskContext,
        TradeSetup tradeSetup,
        DateTimeOffset observedAtUtc)
    {
        // These checks deliberately read like reasons to stand aside.
        if (record.ExecutedTradeCount >= pendingReview.Rules.TradeLimits.MaxDailyTrades)
        {
            return new StandAsideDecision(StandAsideReason.DailyLimitReached, "Daily trade limit reached.", observedAtUtc);
        }

        if (record.ActiveTrade is not null || riskContext.ActiveExposureCount >= pendingReview.Rules.TradeLimits.MaxSimultaneousPositions)
        {
            return new StandAsideDecision(StandAsideReason.ExposureLimitReached, "Existing exposure blocks a fresh trade.", observedAtUtc);
        }

        if (riskContext.OpenPositionInstruments.Contains(tradeSetup.Instrument)
            || riskContext.WorkingOrderInstruments.Contains(tradeSetup.Instrument))
        {
            return new StandAsideDecision(StandAsideReason.DuplicateExposure, "Duplicate or conflicting exposure already exists.", observedAtUtc);
        }

        var snapshot = pendingReview.MarketEvent.Snapshot;
        if (snapshot is not null && snapshot.Spread > pendingReview.Rules.EntryGuards.MaxSpread)
        {
            return new StandAsideDecision(StandAsideReason.SpreadTooWide, "Current spread exceeds the allowed threshold.", observedAtUtc);
        }

        if (snapshot is not null && Math.Abs(snapshot.LastPrice - tradeSetup.EntryPrice) > pendingReview.Rules.EntryGuards.MaxSlippage)
        {
            return new StandAsideDecision(StandAsideReason.SlippageTooHigh, "Estimated entry slippage exceeds the allowed threshold.", observedAtUtc);
        }

        var eventWindowBlocked = record.Plan!.CalendarEvents.Any(x =>
            x.Impact == EconomicEventImpact.High
            && x.AffectedInstruments.Contains(tradeSetup.Instrument)
            && x.ScheduledAtUtc >= observedAtUtc
            && x.ScheduledAtUtc - observedAtUtc <= pendingReview.Rules.EntryGuards.BlockBeforeHighImpactEvent);

        if (eventWindowBlocked)
        {
            return new StandAsideDecision(StandAsideReason.EventWindowBlocked, "A high-impact event is too close to open a fresh trade.", observedAtUtc);
        }

        var riskPerUnit = tradeSetup.Direction == TradeDirection.Buy
            ? tradeSetup.EntryPrice - tradeSetup.StopPrice
            : tradeSetup.StopPrice - tradeSetup.EntryPrice;

        var rewardPerUnit = tradeSetup.Direction == TradeDirection.Buy
            ? tradeSetup.TargetPrice - tradeSetup.EntryPrice
            : tradeSetup.EntryPrice - tradeSetup.TargetPrice;

        if (riskPerUnit <= 0m || rewardPerUnit <= 0m)
        {
            return new StandAsideDecision(StandAsideReason.NoSetup, "Trade prices do not form a valid setup.", observedAtUtc);
        }

        var rewardRiskRatio = rewardPerUnit / riskPerUnit;
        return rewardRiskRatio < pendingReview.Rules.Risk.MinimumRewardRiskRatio
            ? new StandAsideDecision(StandAsideReason.RewardRiskTooLow, "Reward:risk is below the required threshold.", observedAtUtc)
            : null;
    }

    private ApprovedTrade? TryCreateApprovedTrade(
        TradeSetup tradeSetup,
        RiskContext riskContext,
        StrategyRules rules,
        TradeApproval approval)
    {
        var riskPerUnit = tradeSetup.Direction == TradeDirection.Buy
            ? tradeSetup.EntryPrice - tradeSetup.StopPrice
            : tradeSetup.StopPrice - tradeSetup.EntryPrice;

        var rewardPerUnit = tradeSetup.Direction == TradeDirection.Buy
            ? tradeSetup.TargetPrice - tradeSetup.EntryPrice
            : tradeSetup.EntryPrice - tradeSetup.TargetPrice;

        var quantity = _positionSizer.SizePosition(riskContext.AccountEquity, riskContext.AvailableRiskBudget, riskPerUnit, rules.Risk);
        if (quantity <= 0m)
        {
            return null;
        }

        var riskAmount = Math.Min(riskContext.AccountEquity * rules.Risk.RiskPerTradeFraction, riskContext.AvailableRiskBudget);
        var rewardRiskRatio = rewardPerUnit / riskPerUnit;

        return new ApprovedTrade(
            tradeSetup.Instrument,
            tradeSetup.Direction,
            quantity,
            tradeSetup.EntryPrice,
            tradeSetup.StopPrice,
            tradeSetup.TargetPrice,
            riskAmount,
            rewardRiskRatio,
            approval.ApprovedAtUtc);
    }
}
