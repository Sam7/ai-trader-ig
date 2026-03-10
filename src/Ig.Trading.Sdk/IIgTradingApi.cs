using Ig.Trading.Sdk.Auth;
using Ig.Trading.Sdk.Models;

namespace Ig.Trading.Sdk;

public interface IIgTradingApi
{
    Task<IgSessionContext> AuthenticateAsync(CancellationToken cancellationToken = default);

    Task<MarketDetailsResponse> GetMarketByEpicAsync(string epic, CancellationToken cancellationToken = default);

    Task<CreatePositionResponse> CreatePositionAsync(CreatePositionRequest request, CancellationToken cancellationToken = default);

    Task<ClosePositionResponse> ClosePositionAsync(ClosePositionRequest request, CancellationToken cancellationToken = default);

    Task<PositionsResponse> GetOpenPositionsAsync(CancellationToken cancellationToken = default);

    Task<DealConfirmationResponse?> GetDealConfirmationAsync(string dealReference, CancellationToken cancellationToken = default);

    Task<WorkingOrdersResponse> GetWorkingOrdersAsync(CancellationToken cancellationToken = default);

    Task<ActivityResponse> GetActivityAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int pageSize,
        CancellationToken cancellationToken = default);
}
