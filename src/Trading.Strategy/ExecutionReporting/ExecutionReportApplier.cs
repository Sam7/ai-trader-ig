using Trading.Strategy.Persistence;
using Trading.Strategy.Shared;

namespace Trading.Strategy.ExecutionReporting;

/// <summary>
/// Reconciles what the outside execution layer says happened.
/// This is the handoff from planned intent into actual lifecycle state: pending trade, live trade, or closed-out trade.
/// </summary>
public sealed class ExecutionReportApplier
{
    private readonly ITradingDayStore _tradingDayStore;

    public ExecutionReportApplier(ITradingDayStore tradingDayStore)
    {
        _tradingDayStore = tradingDayStore;
    }

    public async Task<TradingDayStatus> ApplyAsync(ExecutionReport report, CancellationToken cancellationToken = default)
    {
        var tradingDate = DateOnly.FromDateTime(report.OccurredAtUtc.UtcDateTime);
        var record = await _tradingDayStore.GetAsync(tradingDate, cancellationToken)
            ?? throw new InvalidOperationException("Trading day has not been planned.");

        var nextPendingTrade = record.PendingTrade;
        var nextActiveTrade = record.ActiveTrade;
        var nextTradeCount = record.ExecutedTradeCount;

        // Pending trades become active only when execution confirms they actually reached the market.
        if (record.PendingTrade is not null && record.PendingTrade.Instrument == report.Instrument)
        {
            if (report.Type is ExecutionReportType.Submitted or ExecutionReportType.Filled or ExecutionReportType.PartiallyFilled)
            {
                if (record.ActiveTrade is null)
                {
                    nextTradeCount++;
                }

                nextActiveTrade = new ActiveTrade(
                    record.PendingTrade.Instrument,
                    record.PendingTrade.Direction,
                    report.FilledQuantity ?? record.PendingTrade.Quantity,
                    record.PendingTrade.EntryPrice,
                    record.PendingTrade.StopPrice,
                    record.PendingTrade.TargetPrice,
                    record.PendingTrade.RiskAmount,
                    record.PendingTrade.RewardRiskRatio,
                    ToLifecycle(report.Type),
                    report.OccurredAtUtc,
                    report.BrokerReference,
                    report.FilledQuantity);
                nextPendingTrade = null;
            }
            else if (report.Type == ExecutionReportType.Rejected)
            {
                nextPendingTrade = null;
            }
        }
        else if (record.ActiveTrade is not null && record.ActiveTrade.Instrument == report.Instrument)
        {
            // Once a trade is live, execution reports only advance or close its lifecycle.
            nextActiveTrade = report.Type is ExecutionReportType.StoppedOut or ExecutionReportType.TargetHit or ExecutionReportType.Closed
                ? null
                : record.ActiveTrade with
                {
                    Lifecycle = ToLifecycle(report.Type),
                    UpdatedAtUtc = report.OccurredAtUtc,
                    BrokerReference = report.BrokerReference ?? record.ActiveTrade.BrokerReference,
                    FilledQuantity = report.FilledQuantity ?? record.ActiveTrade.FilledQuantity,
                };
        }

        var nextRecord = record.ApplyExecution(nextActiveTrade, nextPendingTrade, nextTradeCount);
        await _tradingDayStore.SaveAsync(nextRecord, cancellationToken);
        return new TradingDayStatus(nextRecord.TradingDate, nextRecord.Plan, nextRecord.ExecutedTradeCount, nextRecord.PendingTrade, nextRecord.ActiveTrade);
    }

    private static TradeLifecycle ToLifecycle(ExecutionReportType reportType) => reportType switch
    {
        ExecutionReportType.Submitted => TradeLifecycle.Submitted,
        ExecutionReportType.Filled => TradeLifecycle.Filled,
        ExecutionReportType.PartiallyFilled => TradeLifecycle.PartiallyFilled,
        ExecutionReportType.Amended => TradeLifecycle.Amended,
        ExecutionReportType.StoppedOut => TradeLifecycle.StoppedOut,
        ExecutionReportType.TargetHit => TradeLifecycle.TargetHit,
        ExecutionReportType.Closed => TradeLifecycle.Closed,
        _ => TradeLifecycle.Submitted,
    };
}
