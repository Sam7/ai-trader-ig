using Ig.Trading.Sdk.Models;
using Refit;

namespace Ig.Trading.Sdk.Contracts;

public interface IIgMarketsApi
{
    [Get("/markets/{epic}")]
    [Headers("Version: 4")]
    Task<MarketDetailsResponse> GetMarketByEpicAsync(string epic, CancellationToken cancellationToken = default);

    [Get("/markets")]
    [Headers("Version: 1")]
    Task<MarketSearchResponse> SearchMarketsAsync([AliasAs("searchTerm")] string searchTerm, CancellationToken cancellationToken = default);

    [Get("/marketnavigation")]
    [Headers("Version: 1")]
    Task<MarketNavigationResponse> GetMarketNavigationRootAsync(CancellationToken cancellationToken = default);

    [Get("/marketnavigation/{nodeId}")]
    [Headers("Version: 1")]
    Task<MarketNavigationResponse> GetMarketNavigationNodeAsync(string nodeId, CancellationToken cancellationToken = default);

    [Get("/prices/{epic}")]
    [Headers("Version: 3")]
    Task<PricesResponse> GetRecentPricesAsync(string epic, CancellationToken cancellationToken = default);

    [Get("/prices/{epic}/{resolution}/{numPoints}")]
    [Headers("Version: 2")]
    Task<PricesResponse> GetPricesByPointsAsync(string epic, string resolution, int numPoints, CancellationToken cancellationToken = default);

    [Get("/prices/{epic}/{resolution}/{from}/{to}")]
    [Headers("Version: 2")]
    Task<PricesResponse> GetPricesByRangeAsync(string epic, string resolution, string from, string to, CancellationToken cancellationToken = default);
}
