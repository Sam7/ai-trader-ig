using Trading.Abstractions;
using Trading.Strategy.Configuration;
using Trading.Strategy.Context;

namespace Trading.Strategy.Execution;

public sealed class ExecutionPolicy
{
    public ExecutionPolicyDecision BuildIntent(
        StrategyProfile profile,
        TradingDayState state,
        ExposureState exposureState,
        TradeProposal proposal,
        MarketSnapshot? currentSnapshot,
        IReadOnlyList<EconomicEvent> scheduledEvents,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(exposureState);
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(scheduledEvents);

        proposal.Validate();
        profile.Validate();

        if (state.DailyTradeCount >= profile.TradeLimitsPolicy.MaxDailyTrades)
        {
            return Reject(NoTradeReasonCode.DailyLimitReached, "Daily trade limit reached.", nowUtc);
        }

        if (state.OpenTrade is not null || exposureState.ActiveExposureCount >= profile.TradeLimitsPolicy.MaxSimultaneousPositions)
        {
            return Reject(NoTradeReasonCode.ExposureLimitReached, "Existing exposure blocks new entries.", nowUtc);
        }

        if (exposureState.OpenPositionInstruments.Contains(proposal.Instrument)
            || exposureState.WorkingOrderInstruments.Contains(proposal.Instrument))
        {
            return Reject(NoTradeReasonCode.DuplicateExposure, "Duplicate or conflicting exposure already exists.", nowUtc);
        }

        var activeEvent = scheduledEvents.Any(x =>
            x.Impact == EconomicEventImpact.High
            && x.AffectedInstruments.Contains(proposal.Instrument)
            && x.ScheduledAtUtc >= nowUtc
            && x.ScheduledAtUtc - nowUtc <= profile.NoTradeWindowPolicy.BlockBeforeHighImpactEvent);

        if (activeEvent)
        {
            return Reject(NoTradeReasonCode.EventWindowBlocked, "A high-impact event is too close to open a fresh trade.", nowUtc);
        }

        if (currentSnapshot is not null && currentSnapshot.Spread > profile.NoTradeWindowPolicy.MaxSpread)
        {
            return Reject(NoTradeReasonCode.SpreadTooWide, "Current spread exceeds the allowed threshold.", nowUtc);
        }

        if (currentSnapshot is not null && Math.Abs(currentSnapshot.LastPrice - proposal.EntryPrice) > profile.NoTradeWindowPolicy.MaxSlippage)
        {
            return Reject(NoTradeReasonCode.SlippageTooHigh, "Estimated entry slippage exceeds the allowed threshold.", nowUtc);
        }

        var riskPerUnit = proposal.Direction == TradeDirection.Buy
            ? proposal.EntryPrice - proposal.StopPrice
            : proposal.StopPrice - proposal.EntryPrice;

        var rewardPerUnit = proposal.Direction == TradeDirection.Buy
            ? proposal.TargetPrice - proposal.EntryPrice
            : proposal.EntryPrice - proposal.TargetPrice;

        if (riskPerUnit <= 0m || rewardPerUnit <= 0m)
        {
            return Reject(NoTradeReasonCode.NoSetup, "Proposal prices do not form a valid trade setup.", nowUtc);
        }

        var rewardRiskRatio = rewardPerUnit / riskPerUnit;
        if (rewardRiskRatio < profile.RiskPolicy.MinimumRewardRiskRatio)
        {
            return Reject(NoTradeReasonCode.RewardRiskTooLow, "Reward:risk is below the required threshold.", nowUtc);
        }

        var riskAmount = exposureState.AccountEquity * profile.RiskPolicy.RiskPerTradeFraction;
        if (riskAmount <= 0m || exposureState.AvailableRiskBudget <= 0m)
        {
            return Reject(NoTradeReasonCode.ExposureLimitReached, "No usable risk budget is available.", nowUtc);
        }

        riskAmount = Math.Min(riskAmount, exposureState.AvailableRiskBudget);
        var quantity = decimal.Round(riskAmount / riskPerUnit, 4, MidpointRounding.ToZero);
        if (quantity <= 0m)
        {
            return Reject(NoTradeReasonCode.NoSetup, "Computed trade size rounded down to zero.", nowUtc);
        }

        return ExecutionPolicyDecision.Approved(
            new ExecutionIntent(
                proposal.Instrument,
                proposal.Direction,
                quantity,
                proposal.EntryPrice,
                proposal.StopPrice,
                proposal.TargetPrice,
                riskAmount,
                rewardRiskRatio,
                nowUtc));
    }

    private static ExecutionPolicyDecision Reject(NoTradeReasonCode code, string summary, DateTimeOffset nowUtc)
        => ExecutionPolicyDecision.Rejected(new NoTradeDecision(code, summary, nowUtc));
}
