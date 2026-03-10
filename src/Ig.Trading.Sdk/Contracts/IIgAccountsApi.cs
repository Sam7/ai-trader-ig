using Ig.Trading.Sdk.Models;
using Refit;

namespace Ig.Trading.Sdk.Contracts;

public interface IIgAccountsApi
{
    [Get("/accounts")]
    [Headers("Version: 1")]
    Task<AccountsResponse> GetAccountsAsync(CancellationToken cancellationToken = default);
}
