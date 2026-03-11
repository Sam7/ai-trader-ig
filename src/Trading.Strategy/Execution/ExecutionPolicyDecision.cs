namespace Trading.Strategy.Execution;

public sealed record ExecutionPolicyDecision(
    ExecutionIntent? ExecutionIntent,
    NoTradeDecision? NoTradeDecision)
{
    public static ExecutionPolicyDecision Approved(ExecutionIntent executionIntent) => new(executionIntent, null);

    public static ExecutionPolicyDecision Rejected(NoTradeDecision noTradeDecision) => new(null, noTradeDecision);
}
