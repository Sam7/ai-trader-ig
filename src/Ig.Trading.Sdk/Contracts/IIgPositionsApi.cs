using Ig.Trading.Sdk.Models;
using Refit;

namespace Ig.Trading.Sdk.Contracts;

public interface IIgPositionsApi
{
    [Post("/positions/otc")]
    [Headers("Version: 2")]
    Task<CreatePositionResponse> CreatePositionAsync([Body] CreatePositionRequest request, CancellationToken cancellationToken = default);

    [Delete("/positions/otc")]
    [Headers("Version: 1")]
    Task<ClosePositionResponse> ClosePositionAsync([Body] ClosePositionRequest request, CancellationToken cancellationToken = default);

    [Get("/positions")]
    [Headers("Version: 2")]
    Task<PositionsResponse> GetOpenPositionsAsync(CancellationToken cancellationToken = default);
}
