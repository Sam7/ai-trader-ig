namespace Trading.IG;

public interface IOrderReferenceJournal
{
    Task SaveAsync(OrderSubmissionRecord record, CancellationToken cancellationToken = default);

    Task<OrderSubmissionRecord?> GetAsync(string dealReference, CancellationToken cancellationToken = default);
}
