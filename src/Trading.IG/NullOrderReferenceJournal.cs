namespace Trading.IG;

public sealed class NullOrderReferenceJournal : IOrderReferenceJournal
{
    public Task SaveAsync(OrderSubmissionRecord record, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<OrderSubmissionRecord?> GetAsync(string dealReference, CancellationToken cancellationToken = default)
        => Task.FromResult<OrderSubmissionRecord?>(null);
}
