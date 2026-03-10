using Ig.Trading.Sdk.Models;
using Refit;

namespace Ig.Trading.Sdk.Contracts;

public interface IIgMarketsApi
{
    [Get("/markets/{epic}")]
    [Headers("Version: 4")]
    Task<MarketDetailsResponse> GetMarketByEpicAsync(string epic, CancellationToken cancellationToken = default);
}
