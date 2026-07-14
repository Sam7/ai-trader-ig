namespace Trading.Automation.Execution;

public interface IIntradayOpportunityPreparationStore
{
    Task<IntradayOpportunityPreparationDocument> WriteAsync(
        DateOnly tradingDate,
        DateTimeOffset requestedAtUtc,
        IntradayPreparedRun preparedRun,
        CancellationToken cancellationToken = default);

    Task<IntradayOpportunityPreparationDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken = default);
}
