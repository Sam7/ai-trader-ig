using Trading.Strategy.Context;

namespace Trading.Strategy.Workflow;

public interface ITradingStrategyDirector
{
    Task<DailyBriefing> PrepareTradingDayAsync(TradingDayRequest request, CancellationToken cancellationToken = default);

    Task<MarketReaction> ReactToMarketEventAsync(MarketEvent marketEvent, CancellationToken cancellationToken = default);

    Task<TradeManagementDecision> ReviewOpenTradeAsync(OpenTradeReviewRequest request, CancellationToken cancellationToken = default);

    Task<TradingDayState> RecordExecutionOutcomeAsync(ExecutionOutcome outcome, CancellationToken cancellationToken = default);
}
