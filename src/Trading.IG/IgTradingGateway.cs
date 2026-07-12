using Ig.Trading.Sdk;
using Ig.Trading.Sdk.Errors;
using Ig.Trading.Sdk.Models;
using Microsoft.Extensions.Logging;
using Trading.Abstractions;
using SdkClosePositionRequest = Ig.Trading.Sdk.Models.ClosePositionRequest;
using SdkGetPricesRequest = Ig.Trading.Sdk.Models.GetPricesRequest;
using SdkUpdatePositionRequest = Ig.Trading.Sdk.Models.UpdatePositionRequest;

namespace Trading.IG;

public sealed class IgTradingGateway : ITradingGateway
{
    private readonly IIgTradingApi _igTradingApi;
    private readonly IgOrderStatusResolver _orderStatusResolver;

    public IgTradingGateway(
        IIgTradingApi igTradingApi,
        ILogger<IgTradingGateway> logger)
    {
        _igTradingApi = igTradingApi;
        _orderStatusResolver = new IgOrderStatusResolver(igTradingApi, logger);
    }

    public async Task<ITradingSession> AuthenticateAsync(CancellationToken cancellationToken = default)
        => await ExecuteTranslatedAsync(
            async () =>
        {
            var session = await _igTradingApi.AuthenticateAsync(cancellationToken);
            return new IgTradingSession(session.CurrentAccountId ?? string.Empty, session.AuthenticatedAtUtc ?? DateTimeOffset.UtcNow);
        });

    public async Task<PlaceOrderResult> PlaceMarketOrderAsync(
        PlaceOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Size), "Order size must be greater than zero.");
        }

        if (request.StopLevel is null ^ request.LimitLevel is null)
        {
            throw new ArgumentException("StopLevel and LimitLevel must either both be supplied or both be omitted.", nameof(request));
        }

        var dealReference = ResolveDealReference(request.DealReference, "SPIKE");

        return await ExecuteTranslatedAsync(
            async () =>
        {
            var market = await _igTradingApi.GetMarketByEpicAsync(request.Instrument.Value, cancellationToken);
            IgTradingConversions.EnsureMarketIsTradable(market);

            var currencyCode = IgTradingConversions.ResolveCurrencyCode(market);

            await _igTradingApi.CreatePositionAsync(
                new CreatePositionRequest(
                    request.Instrument.Value,
                    market.Instrument.Expiry,
                    IgTradingConversions.ToIgDirection(request.Direction),
                    request.Size,
                    "MARKET",
                    currencyCode,
                    "FILL_OR_KILL",
                    ForceOpen: true,
                    GuaranteedStop: false,
                    dealReference,
                    StopLevel: request.StopLevel,
                    LimitLevel: request.LimitLevel),
                cancellationToken);

            var summary = await _orderStatusResolver.GetOrderStatusAsync(dealReference, cancellationToken);
            return new PlaceOrderResult(
                dealReference,
                summary?.DealId,
                summary?.Status ?? OrderStatus.Pending,
                summary?.Message,
                summary?.TimestampUtc ?? DateTimeOffset.UtcNow);
        });
    }

    public async Task<WorkingOrderResult> PlaceWorkingOrderAsync(
        Trading.Abstractions.CreateWorkingOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Size), "Working order size must be greater than zero.");
        }

        return await ExecuteTranslatedAsync(
            async () =>
        {
            var market = await _igTradingApi.GetMarketByEpicAsync(request.Instrument.Value, cancellationToken);
            IgTradingConversions.EnsureMarketIsTradable(market);

            var response = await _igTradingApi.CreateWorkingOrderAsync(
                new Ig.Trading.Sdk.Models.CreateWorkingOrderRequest(
                    request.Instrument.Value,
                    market.Instrument.Expiry,
                    IgTradingConversions.ToIgDirection(request.Direction),
                    request.Size,
                    request.Level,
                    IgTradingConversions.ToIgWorkingOrderType(request.Type),
                    IgTradingConversions.ResolveCurrencyCode(market),
                    GuaranteedStop: false,
                    IgTradingConversions.ToIgTimeInForce(request.TimeInForce),
                    IgTradingConversions.ToIgGoodTillDate(request.GoodTillDateUtc)),
                cancellationToken);

            return new WorkingOrderResult(response.DealReference, null, OrderStatus.Accepted, "Working order submitted.", DateTimeOffset.UtcNow);
        });
    }

    public async Task<ClosePositionResult> ClosePositionAsync(
        Trading.Abstractions.ClosePositionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DealId))
        {
            throw new ArgumentException("DealId is required.", nameof(request.DealId));
        }

        var dealReference = ResolveDealReference(request.DealReference, "CLOSE");

        return await ExecuteTranslatedAsync(
            async () =>
        {
            var openPositions = await _igTradingApi.GetOpenPositionsAsync(cancellationToken);
            var position = openPositions.Positions?.FirstOrDefault(x =>
                string.Equals(x.Position.DealId, request.DealId, StringComparison.OrdinalIgnoreCase));

            if (position is null)
            {
                throw new TradingGatewayException(TradingErrorCode.InvalidRequest, $"No open position found for dealId '{request.DealId}'.");
            }

            var closeSize = request.Size ?? position.Position.Size;
            if (closeSize <= 0 || closeSize > position.Position.Size)
            {
                throw new TradingGatewayException(TradingErrorCode.InvalidRequest, "Close size must be greater than zero and not exceed current position size.");
            }

            await _igTradingApi.ClosePositionAsync(
                new SdkClosePositionRequest(
                    request.DealId,
                    IgTradingConversions.ToOppositeDirection(position.Position.Direction),
                    closeSize,
                    "MARKET",
                    "FILL_OR_KILL",
                    dealReference),
                cancellationToken);

            var status = await _orderStatusResolver.GetOrderStatusAsync(dealReference, cancellationToken);
            return new ClosePositionResult(
                dealReference,
                status?.DealId,
                status?.Status ?? OrderStatus.Pending,
                status?.Message,
                status?.TimestampUtc ?? DateTimeOffset.UtcNow);
        });
    }

    public async Task<UpdatePositionResult> UpdatePositionAsync(
        Trading.Abstractions.UpdatePositionRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Validate();

        return await ExecuteTranslatedAsync(
            async () =>
        {
            var existing = await _igTradingApi.GetPositionByDealIdAsync(request.DealId, cancellationToken);
            if (existing is null)
            {
                throw new TradingGatewayException(TradingErrorCode.InvalidRequest, $"No open position found for dealId '{request.DealId}'.");
            }

            var response = await _igTradingApi.UpdatePositionAsync(
                request.DealId,
                new SdkUpdatePositionRequest(
                    request.LimitLevel ?? existing.Position.LimitLevel,
                    request.StopLevel ?? existing.Position.StopLevel,
                    request.TrailingStopDistance is not null || existing.Position.TrailingStopDistance is not null,
                    request.TrailingStopDistance ?? existing.Position.TrailingStopDistance,
                    request.TrailingStopIncrement ?? existing.Position.TrailingStopIncrement),
                cancellationToken);

            var status = await _orderStatusResolver.GetOrderStatusAsync(response.DealReference, cancellationToken);
            return new UpdatePositionResult(
                response.DealReference,
                request.DealId,
                status?.Status ?? OrderStatus.Pending,
                status?.Message ?? "Position amendment submitted.",
                status?.TimestampUtc ?? DateTimeOffset.UtcNow);
        });
    }

    public async Task<WorkingOrderResult> UpdateWorkingOrderAsync(
        Trading.Abstractions.UpdateWorkingOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DealId))
        {
            throw new ArgumentException("DealId is required.", nameof(request.DealId));
        }

        return await ExecuteTranslatedAsync(
            async () =>
        {
            var existing = await _igTradingApi.GetWorkingOrdersAsync(cancellationToken);
            var workingOrder = (existing.WorkingOrders ?? [])
                .FirstOrDefault(x => string.Equals(x.WorkingOrderData.DealId, request.DealId, StringComparison.OrdinalIgnoreCase));

            if (workingOrder is null)
            {
                throw new TradingGatewayException(TradingErrorCode.InvalidRequest, $"No working order found for dealId '{request.DealId}'.");
            }

            var response = await _igTradingApi.UpdateWorkingOrderAsync(
                request.DealId,
                new Ig.Trading.Sdk.Models.UpdateWorkingOrderRequest(
                    request.Level ?? workingOrder.WorkingOrderData.OrderLevel,
                    IgTradingConversions.ToIgTimeInForce(request.TimeInForce ?? IgTradingConversions.ParseTimeInForce(workingOrder.WorkingOrderData.TimeInForce)),
                    IgTradingConversions.ToIgWorkingOrderType(request.Type ?? IgTradingConversions.ParseWorkingOrderType(workingOrder.WorkingOrderData.OrderType)),
                    IgTradingConversions.ToIgGoodTillDate(request.GoodTillDateUtc ?? IgTradingConversions.ParseNullableDate(workingOrder.WorkingOrderData.GoodTillDateIso ?? workingOrder.WorkingOrderData.GoodTillDate))),
                cancellationToken);

            return new WorkingOrderResult(response.DealReference, request.DealId, OrderStatus.Accepted, "Working order updated.", DateTimeOffset.UtcNow);
        });
    }

    public async Task<WorkingOrderResult> CancelWorkingOrderAsync(
        string dealId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dealId))
        {
            throw new ArgumentException("DealId is required.", nameof(dealId));
        }

        return await ExecuteTranslatedAsync(
            async () =>
        {
            var response = await _igTradingApi.DeleteWorkingOrderAsync(dealId, cancellationToken);

            return new WorkingOrderResult(response.DealReference, dealId, OrderStatus.Accepted, "Working order cancelled.", DateTimeOffset.UtcNow);
        });
    }

    public async Task<IReadOnlyList<PositionSummary>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
        => await ExecuteTranslatedAsync(
            async () =>
        {
            var response = await _igTradingApi.GetOpenPositionsAsync(cancellationToken);
            return (response.Positions ?? [])
                .Select(IgTradingMapper.MapPosition)
                .ToList();
        });

    public async Task<IReadOnlyList<WorkingOrderSummary>> GetWorkingOrdersAsync(CancellationToken cancellationToken = default)
        => await ExecuteTranslatedAsync(
            async () =>
        {
            var response = await _igTradingApi.GetWorkingOrdersAsync(cancellationToken);
            return (response.WorkingOrders ?? [])
                .Select(IgTradingMapper.MapWorkingOrder)
                .ToList();
        });

    public async Task<IReadOnlyList<MarketSearchResult>> SearchMarketsAsync(
        string searchTerm,
        int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            throw new ArgumentException("Search term is required.", nameof(searchTerm));
        }

        if (maxResults <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), "MaxResults must be greater than zero.");
        }

        return await ExecuteTranslatedAsync(
            async () =>
        {
            var response = await _igTradingApi.SearchMarketsAsync(searchTerm, cancellationToken);
            return (response.Markets ?? [])
                .Take(maxResults)
                .Select(IgTradingMapper.MapMarketSearchResult)
                .ToList();
        });
    }

    public async Task<MarketDetails> GetMarketDetailsAsync(
        InstrumentId instrument,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instrument.Value))
        {
            throw new ArgumentException("Instrument is required.", nameof(instrument));
        }

        return await ExecuteTranslatedAsync(
            async () =>
        {
            var response = await _igTradingApi.GetMarketByEpicAsync(instrument.Value, cancellationToken);
            return IgTradingMapper.MapMarketDetails(response);
        });
    }

    public async Task<MarketNavigationPage> BrowseMarketsAsync(
        string? nodeId = null,
        CancellationToken cancellationToken = default)
        => await ExecuteTranslatedAsync(
            async () =>
        {
            var response = await _igTradingApi.GetMarketNavigationAsync(nodeId, cancellationToken);
            return IgTradingMapper.MapMarketNavigation(nodeId, response);
        });

    public async Task<PriceSeries> GetPricesAsync(
        Trading.Abstractions.GetPricesRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Validate();

        return await ExecuteTranslatedAsync(
            async () =>
        {
            var response = await _igTradingApi.GetPricesAsync(
                new SdkGetPricesRequest(
                    request.Instrument.Value,
                    request.Resolution is null ? null : IgTradingConversions.ToIgPriceResolution(request.Resolution.Value),
                    request.MaxPoints,
                    request.FromUtc,
                    request.ToUtc),
                cancellationToken);

            return IgTradingMapper.MapPrices(request, response);
        });
    }

    public async Task<IReadOnlyList<OrderSummary>> GetOrdersAsync(
        OrderQuery query,
        CancellationToken cancellationToken = default)
    {
        query.Validate();

        return await ExecuteTranslatedAsync(
            async () =>
        {
            var response = await _igTradingApi.GetActivityAsync(query.FromUtc, query.ToUtc, query.MaxItems, cancellationToken);

            return (response.Activities ?? [])
                .Where(activity => !string.IsNullOrWhiteSpace(IgTradingMapper.ResolveActivityDealReference(activity)) || !string.IsNullOrWhiteSpace(activity.DealId))
                .Select(IgTradingMapper.MapActivity)
                .ToList();
        });
    }

    public Task<OrderSummary?> GetOrderStatusAsync(
        string dealReference,
        CancellationToken cancellationToken = default)
        => _orderStatusResolver.GetOrderStatusAsync(dealReference, cancellationToken);

    internal static TradingGatewayException TranslateException(IgApiException exception)
    {
        var code = MapErrorCode(exception.ErrorCode);
        return new TradingGatewayException(code, exception.Message, exception);
    }

    private static async Task<T> ExecuteTranslatedAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (IgApiException exception)
        {
            throw TranslateException(exception);
        }
    }

    private static string ResolveDealReference(string? requestedDealReference, string prefix)
    {
        if (string.IsNullOrWhiteSpace(requestedDealReference))
        {
            return IgTradingConversions.CreateDealReference(prefix);
        }

        if (requestedDealReference.Length > 30
            || requestedDealReference.Any(character => !char.IsAsciiLetterOrDigit(character))
            || requestedDealReference.Any(char.IsLower))
        {
            throw new ArgumentException("Deal reference must contain 1-30 uppercase ASCII letters or digits.", nameof(requestedDealReference));
        }

        return requestedDealReference;
    }

    private static TradingErrorCode MapErrorCode(string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            return TradingErrorCode.BrokerError;
        }

        if (errorCode.Contains("auth", StringComparison.OrdinalIgnoreCase)
            || errorCode.Contains("security", StringComparison.OrdinalIgnoreCase))
        {
            return TradingErrorCode.AuthenticationFailed;
        }

        if (errorCode.Contains("session", StringComparison.OrdinalIgnoreCase)
            || errorCode.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            return TradingErrorCode.SessionExpired;
        }

        if (errorCode.Contains("epic", StringComparison.OrdinalIgnoreCase)
            || errorCode.Contains("instrument", StringComparison.OrdinalIgnoreCase)
            || errorCode.Contains("market", StringComparison.OrdinalIgnoreCase))
        {
            return TradingErrorCode.InvalidInstrument;
        }

        if (errorCode.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            return TradingErrorCode.MarketClosed;
        }

        if (errorCode.Contains("margin", StringComparison.OrdinalIgnoreCase)
            || errorCode.Contains("fund", StringComparison.OrdinalIgnoreCase))
        {
            return TradingErrorCode.InsufficientFunds;
        }

        if (errorCode.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            || errorCode.Contains("validation", StringComparison.OrdinalIgnoreCase))
        {
            return TradingErrorCode.InvalidRequest;
        }

        return TradingErrorCode.BrokerError;
    }
}
