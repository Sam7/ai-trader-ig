using Ig.Trading.Sdk.Auth;
using Ig.Trading.Sdk.Configuration;
using Ig.Trading.Sdk.Contracts;
using Ig.Trading.Sdk.Errors;
using Ig.Trading.Sdk.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Refit;
using System.Net;
using System.Net.Http.Headers;

namespace Ig.Trading.Sdk;

internal sealed class IgTradingApi : IIgTradingApi
{
    private readonly IIgSessionApi _sessionApi;
    private readonly IIgMarketsApi _marketsApi;
    private readonly IIgPositionsApi _positionsApi;
    private readonly IIgOrderStateApi _orderStateApi;
    private readonly IIgWorkingOrdersApi _workingOrdersApi;
    private readonly IIgAccountsApi _accountsApi;
    private readonly IIgSessionStore _sessionStore;
    private readonly IIgPasswordEncryptor _passwordEncryptor;
    private readonly IgClientOptions _options;
    private readonly ILogger<IgTradingApi> _logger;

    internal IgTradingApi(
        IIgSessionApi sessionApi,
        IIgMarketsApi marketsApi,
        IIgPositionsApi positionsApi,
        IIgOrderStateApi orderStateApi,
        IIgWorkingOrdersApi workingOrdersApi,
        IIgAccountsApi accountsApi,
        IIgSessionStore sessionStore,
        IIgPasswordEncryptor passwordEncryptor,
        IOptions<IgClientOptions> options,
        ILogger<IgTradingApi> logger)
    {
        _sessionApi = sessionApi;
        _marketsApi = marketsApi;
        _positionsApi = positionsApi;
        _orderStateApi = orderStateApi;
        _workingOrdersApi = workingOrdersApi;
        _accountsApi = accountsApi;
        _sessionStore = sessionStore;
        _passwordEncryptor = passwordEncryptor;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IgSessionContext> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        _options.Validate();

        var sessionRequest = await CreateSessionRequestAsync(cancellationToken);
        var response = await ExecuteAsync(
            () => _sessionApi.CreateSessionAsync(sessionRequest, cancellationToken));

        EnsureSuccess(response);

        var cst = TryGetSingleHeader(response.Headers, "CST");
        var securityToken = TryGetSingleHeader(response.Headers, "X-SECURITY-TOKEN");
        var currentAccountId = response.Content?.CurrentAccountId;

        if (string.IsNullOrWhiteSpace(cst) || string.IsNullOrWhiteSpace(securityToken))
        {
            throw new IgApiException(null, response.StatusCode, "IG authentication succeeded but security tokens were missing.");
        }

        var session = new IgSessionContext(cst, securityToken, currentAccountId, DateTimeOffset.UtcNow);
        _sessionStore.Set(session);

        if (!string.IsNullOrWhiteSpace(_options.AccountId))
        {
            var switchResponse = await ExecuteAsync(
                () => _sessionApi.SwitchAccountAsync(new SwitchAccountRequest(_options.AccountId!), cancellationToken));
            EnsureSuccess(switchResponse);

            session = session with { CurrentAccountId = _options.AccountId };
            _sessionStore.Set(session);
        }

        _logger.LogInformation("IG session authenticated for account {AccountId}.", session.CurrentAccountId);
        return session;
    }

    public Task<MarketDetailsResponse> GetMarketByEpicAsync(string epic, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => _marketsApi.GetMarketByEpicAsync(epic, cancellationToken));

    public Task<MarketSearchResponse> SearchMarketsAsync(string searchTerm, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => _marketsApi.SearchMarketsAsync(searchTerm, cancellationToken));

    public Task<MarketNavigationResponse> GetMarketNavigationAsync(string? nodeId = null, CancellationToken cancellationToken = default)
        => string.IsNullOrWhiteSpace(nodeId)
            ? ExecuteAsync(() => _marketsApi.GetMarketNavigationRootAsync(cancellationToken))
            : ExecuteAsync(() => _marketsApi.GetMarketNavigationNodeAsync(nodeId, cancellationToken));

    public Task<PricesResponse> GetPricesAsync(GetPricesRequest request, CancellationToken cancellationToken = default)
    {
        if (request.FromUtc is not null && request.ToUtc is not null && request.Resolution is not null)
        {
            return ExecuteAsync(() => _marketsApi.GetPricesByRangeAsync(
                request.Epic,
                request.Resolution,
                request.FromUtc.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss"),
                request.ToUtc.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss"),
                cancellationToken));
        }

        if (request.MaxPoints is not null && request.Resolution is not null)
        {
            return ExecuteAsync(() => _marketsApi.GetPricesByPointsAsync(
                request.Epic,
                request.Resolution,
                request.MaxPoints.Value,
                cancellationToken));
        }

        return ExecuteAsync(() => _marketsApi.GetRecentPricesAsync(request.Epic, cancellationToken));
    }

    public Task<CreatePositionResponse> CreatePositionAsync(CreatePositionRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => _positionsApi.CreatePositionAsync(request, cancellationToken));

    public Task<ClosePositionResponse> ClosePositionAsync(ClosePositionRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => _positionsApi.ClosePositionAsync(request, cancellationToken));

    public Task<UpdatePositionResponse> UpdatePositionAsync(string dealId, UpdatePositionRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => _positionsApi.UpdatePositionAsync(dealId, request, cancellationToken));

    public Task<WorkingOrderMutationResponse> CreateWorkingOrderAsync(CreateWorkingOrderRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => _workingOrdersApi.CreateWorkingOrderAsync(request, cancellationToken));

    public Task<WorkingOrderMutationResponse> UpdateWorkingOrderAsync(string dealId, UpdateWorkingOrderRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => _workingOrdersApi.UpdateWorkingOrderAsync(dealId, request, cancellationToken));

    public Task<WorkingOrderMutationResponse> DeleteWorkingOrderAsync(string dealId, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => _workingOrdersApi.DeleteWorkingOrderAsync(dealId, cancellationToken));

    public Task<PositionsResponse> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(() => _positionsApi.GetOpenPositionsAsync(cancellationToken));

    public async Task<PositionEnvelope?> GetPositionByDealIdAsync(string dealId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _positionsApi.GetPositionByDealIdAsync(dealId, cancellationToken);
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (ApiException exception)
        {
            throw IgErrorParser.ToIgApiException(exception);
        }
    }

    public async Task<DealConfirmationResponse?> GetDealConfirmationAsync(string dealReference, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _orderStateApi.GetDealConfirmationAsync(dealReference, cancellationToken);
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (ApiException exception)
        {
            throw IgErrorParser.ToIgApiException(exception);
        }
    }

    public Task<WorkingOrdersResponse> GetWorkingOrdersAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(() => _workingOrdersApi.GetWorkingOrdersAsync(cancellationToken));

    public Task<ActivityResponse> GetActivityAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int pageSize,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(() => _orderStateApi.GetActivityAsync(
            fromUtc.ToString("yyyy-MM-ddTHH:mm:ss"),
            toUtc.ToString("yyyy-MM-ddTHH:mm:ss"),
            detailed: true,
            pageSize,
            cancellationToken));

    public Task<TransactionHistoryResponse> GetTransactionsAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(() => _orderStateApi.GetTransactionsAsync(cancellationToken));

    public Task<AccountsResponse> GetAccountsAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(() => _accountsApi.GetAccountsAsync(cancellationToken));

    private async Task<SessionRequest> CreateSessionRequestAsync(CancellationToken cancellationToken)
    {
        if (!_options.UseEncryptedPassword)
        {
            return new SessionRequest(_options.Identifier, _options.Password);
        }

        var encryptionKey = await ExecuteAsync(() => _sessionApi.GetEncryptionKeyAsync(cancellationToken));
        var encryptedPassword = _passwordEncryptor.Encrypt(_options.Password, encryptionKey);
        return new SessionRequest(_options.Identifier, encryptedPassword, EncryptedPassword: true);
    }

    private static string? TryGetSingleHeader(HttpHeaders headers, string name)
    {
        if (!headers.TryGetValues(name, out var values))
        {
            return null;
        }

        return values.FirstOrDefault();
    }

    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (ApiException exception)
        {
            throw IgErrorParser.ToIgApiException(exception);
        }
    }

    private static void EnsureSuccess<T>(ApiResponse<T> response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.Error is not null)
        {
            throw IgErrorParser.ToIgApiException(response.Error);
        }

        var content = response.Content?.ToString();
        throw IgErrorParser.Create(response.StatusCode, content);
    }
}
