using Trading.Strategy.Inputs;

namespace Trading.Automation.Execution;

public sealed class PassiveRiskContextSource : IRiskContextSource
{
    public Task<RiskContext> GetRiskContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new RiskContext(100_000m, 100_000m, [], []));
}
