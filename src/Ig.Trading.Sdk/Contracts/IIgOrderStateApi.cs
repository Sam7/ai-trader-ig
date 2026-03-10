using Ig.Trading.Sdk.Models;
using Refit;

namespace Ig.Trading.Sdk.Contracts;

public interface IIgOrderStateApi
{
    [Get("/confirms/{dealReference}")]
    [Headers("Version: 1")]
    Task<DealConfirmationResponse> GetDealConfirmationAsync(string dealReference, CancellationToken cancellationToken = default);

    [Get("/history/activity")]
    [Headers("Version: 3")]
    Task<ActivityResponse> GetActivityAsync(
        [AliasAs("from")] string fromUtc,
        [AliasAs("to")] string toUtc,
        [AliasAs("detailed")] bool detailed,
        [AliasAs("pageSize")] int pageSize,
        CancellationToken cancellationToken = default);

    [Get("/history/transactions")]
    [Headers("Version: 2")]
    Task<TransactionHistoryResponse> GetTransactionsAsync(CancellationToken cancellationToken = default);
}
