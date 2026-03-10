using Ig.Trading.Sdk.Auth;
using Ig.Trading.Sdk.Models;

namespace Ig.Trading.Sdk;

public interface IIgTradingApi
{
    Task<IgSessionContext> AuthenticateAsync(CancellationToken cancellationToken = default);

    Task<MarketDetailsResponse> GetMarketByEpicAsync(string epic, CancellationToken cancellationToken = default);

    Task<MarketSearchResponse> SearchMarketsAsync(string searchTerm, CancellationToken cancellationToken = default);

    Task<MarketNavigationResponse> GetMarketNavigationAsync(string? nodeId = null, CancellationToken cancellationToken = default);

    Task<PricesResponse> GetPricesAsync(GetPricesRequest request, CancellationToken cancellationToken = default);

    Task<CreatePositionResponse> CreatePositionAsync(CreatePositionRequest request, CancellationToken cancellationToken = default);

    Task<ClosePositionResponse> ClosePositionAsync(ClosePositionRequest request, CancellationToken cancellationToken = default);

    Task<UpdatePositionResponse> UpdatePositionAsync(string dealId, UpdatePositionRequest request, CancellationToken cancellationToken = default);

    Task<WorkingOrderMutationResponse> CreateWorkingOrderAsync(CreateWorkingOrderRequest request, CancellationToken cancellationToken = default);

    Task<WorkingOrderMutationResponse> UpdateWorkingOrderAsync(string dealId, UpdateWorkingOrderRequest request, CancellationToken cancellationToken = default);

    Task<WorkingOrderMutationResponse> DeleteWorkingOrderAsync(string dealId, CancellationToken cancellationToken = default);

    Task<PositionsResponse> GetOpenPositionsAsync(CancellationToken cancellationToken = default);

    Task<PositionEnvelope?> GetPositionByDealIdAsync(string dealId, CancellationToken cancellationToken = default);

    Task<DealConfirmationResponse?> GetDealConfirmationAsync(string dealReference, CancellationToken cancellationToken = default);

    Task<WorkingOrdersResponse> GetWorkingOrdersAsync(CancellationToken cancellationToken = default);

    Task<ActivityResponse> GetActivityAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<TransactionHistoryResponse> GetTransactionsAsync(CancellationToken cancellationToken = default);

    Task<AccountsResponse> GetAccountsAsync(CancellationToken cancellationToken = default);
}
