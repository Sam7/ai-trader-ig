using Ig.Trading.Sdk.Models;
using Refit;

namespace Ig.Trading.Sdk.Contracts;

public interface IIgPositionsApi
{
    [Post("/positions/otc")]
    [Headers("Version: 2")]
    Task<CreatePositionResponse> CreatePositionAsync([Body] CreatePositionRequest request, CancellationToken cancellationToken = default);

    // IG documents POST + _method: DELETE as the safer fallback for clients/proxies
    // that do not reliably support DELETE requests with a JSON body.
    [Post("/positions/otc")]
    [Headers("Version: 1", "_method: DELETE")]
    Task<ClosePositionResponse> ClosePositionAsync([Body] ClosePositionRequest request, CancellationToken cancellationToken = default);

    [Get("/positions")]
    [Headers("Version: 2")]
    Task<PositionsResponse> GetOpenPositionsAsync(CancellationToken cancellationToken = default);

    [Get("/positions/{dealId}")]
    [Headers("Version: 2")]
    Task<PositionEnvelope> GetPositionByDealIdAsync(string dealId, CancellationToken cancellationToken = default);
}
