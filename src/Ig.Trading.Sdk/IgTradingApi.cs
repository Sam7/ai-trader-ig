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

public sealed class IgTradingApi : IIgTradingApi
{
    private readonly IIgSessionApi _sessionApi;
    private readonly IIgMarketsApi _marketsApi;
    private readonly IIgPositionsApi _positionsApi;
    private readonly IIgOrderStateApi _orderStateApi;
    private readonly IIgSessionStore _sessionStore;
    private readonly IgClientOptions _options;
    private readonly ILogger<IgTradingApi> _logger;

    public IgTradingApi(
        IIgSessionApi sessionApi,
        IIgMarketsApi marketsApi,
        IIgPositionsApi positionsApi,
        IIgOrderStateApi orderStateApi,
        IIgSessionStore sessionStore,
        IOptions<IgClientOptions> options,
        ILogger<IgTradingApi> logger)
    {
        _sessionApi = sessionApi;
        _marketsApi = marketsApi;
        _positionsApi = positionsApi;
        _orderStateApi = orderStateApi;
        _sessionStore = sessionStore;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IgSessionContext> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        _options.Validate();

        var response = await ExecuteAsync(
            () => _sessionApi.CreateSessionAsync(new SessionRequest(_options.Identifier, _options.Password), cancellationToken));

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
            await ExecuteAsync(
                () => _sessionApi.SwitchAccountAsync(new SwitchAccountRequest(_options.AccountId!), cancellationToken));

            session = session with { CurrentAccountId = _options.AccountId };
            _sessionStore.Set(session);
        }

        _logger.LogInformation("IG session authenticated for account {AccountId}.", session.CurrentAccountId);
        return session;
    }

    public Task<MarketDetailsResponse> GetMarketByEpicAsync(string epic, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => _marketsApi.GetMarketByEpicAsync(epic, cancellationToken));

    public Task<CreatePositionResponse> CreatePositionAsync(CreatePositionRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => _positionsApi.CreatePositionAsync(request, cancellationToken));

    public Task<ClosePositionResponse> ClosePositionAsync(ClosePositionRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => _positionsApi.ClosePositionAsync(request, cancellationToken));

    public Task<PositionsResponse> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(() => _positionsApi.GetOpenPositionsAsync(cancellationToken));

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
        => ExecuteAsync(() => _orderStateApi.GetWorkingOrdersAsync(cancellationToken));

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
}
