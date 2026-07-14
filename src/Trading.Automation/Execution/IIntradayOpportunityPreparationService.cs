namespace Trading.Automation.Execution;

public interface IIntradayOpportunityPreparationService
{
    Task<IntradayOpportunityPreparationDocument?> PrepareAsync(
        DateOnly tradingDate,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default);

    Task<IntradayOpportunityPreparationDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken = default);
}
