using Ig.Trading.Sdk;
using Ig.Trading.Sdk.Errors;
using Microsoft.Extensions.Logging;
using Trading.Abstractions;

namespace Trading.IG;

internal sealed class IgOrderStatusResolver
{
    private readonly IIgTradingApi _igTradingApi;
    private readonly IOrderReferenceJournal _orderReferenceJournal;
    private readonly ILogger _logger;

    public IgOrderStatusResolver(
        IIgTradingApi igTradingApi,
        IOrderReferenceJournal orderReferenceJournal,
        ILogger logger)
    {
        _igTradingApi = igTradingApi;
        _orderReferenceJournal = orderReferenceJournal;
        _logger = logger;
    }

    public async Task<OrderSummary?> GetOrderStatusAsync(string dealReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dealReference))
        {
            throw new ArgumentException("Deal reference is required.", nameof(dealReference));
        }

        return await ExecuteTranslatedAsync(
            async () =>
        {
            var confirmation = await _igTradingApi.GetDealConfirmationAsync(dealReference, cancellationToken);
            if (confirmation is not null)
            {
                return IgTradingMapper.MapConfirmation(confirmation, dealReference);
            }

            var now = DateTimeOffset.UtcNow;
            var activities = await _igTradingApi.GetActivityAsync(now.AddHours(-24), now, 200, cancellationToken);
            var activityMatch = (activities.Activities ?? [])
                .FirstOrDefault(x => string.Equals(IgTradingMapper.ResolveActivityDealReference(x), dealReference, StringComparison.OrdinalIgnoreCase));

            if (activityMatch is not null)
            {
                return IgTradingMapper.MapActivity(activityMatch);
            }

            var transactions = await _igTradingApi.GetTransactionsAsync(cancellationToken);
            var transactionMatch = (transactions.Transactions ?? [])
                .FirstOrDefault(x => string.Equals(IgTradingMapper.ResolveTransactionReference(x), dealReference, StringComparison.OrdinalIgnoreCase));

            if (transactionMatch is not null)
            {
                return IgTradingMapper.MapTransaction(transactionMatch, dealReference);
            }

            var submission = await _orderReferenceJournal.GetAsync(dealReference, cancellationToken);
            if (submission is not null)
            {
                var correlatedStatus = IgTradingMapper.CorrelateFromSubmission(submission, activities.Activities ?? []);
                if (correlatedStatus is not null)
                {
                    return correlatedStatus;
                }
            }

            _logger.LogInformation("No confirm/activity found for deal reference {DealReference}; returning pending.", dealReference);
            return new OrderSummary(dealReference, null, null, null, null, OrderStatus.Pending, "Awaiting broker confirmation.", DateTimeOffset.UtcNow);
        });
    }

    private static async Task<T> ExecuteTranslatedAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (IgApiException exception)
        {
            throw IgTradingGateway.TranslateException(exception);
        }
    }
}
