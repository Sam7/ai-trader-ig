using Ig.Trading.Sdk;
using Ig.Trading.Sdk.Errors;
using Ig.Trading.Sdk.Models;
using Microsoft.Extensions.Logging;
using Trading.Abstractions;
using SdkClosePositionRequest = Ig.Trading.Sdk.Models.ClosePositionRequest;

namespace Trading.IG;

public sealed class IgTradingGateway : ITradingGateway
{
    private readonly IIgTradingApi _igTradingApi;
    private readonly ILogger<IgTradingGateway> _logger;

    public IgTradingGateway(IIgTradingApi igTradingApi, ILogger<IgTradingGateway> logger)
    {
        _igTradingApi = igTradingApi;
        _logger = logger;
    }

    public async Task<ITradingSession> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await _igTradingApi.AuthenticateAsync(cancellationToken);
            return new IgTradingSession(session.CurrentAccountId ?? string.Empty, session.AuthenticatedAtUtc ?? DateTimeOffset.UtcNow);
        }
        catch (IgApiException exception)
        {
            throw TranslateException(exception);
        }
    }

    public async Task<PlaceOrderResult> PlaceMarketOrderAsync(
        PlaceOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Size), "Order size must be greater than zero.");
        }

        try
        {
            var market = await _igTradingApi.GetMarketByEpicAsync(request.Instrument.Value, cancellationToken);
            EnsureMarketIsTradable(market);

            var currencyCode = ResolveCurrencyCode(market);
            var dealReference = $"spike-{Guid.NewGuid():N}";

            await _igTradingApi.CreatePositionAsync(
                new CreatePositionRequest(
                    request.Instrument.Value,
                    market.Instrument.Expiry,
                    ToIgDirection(request.Direction),
                    request.Size,
                    "MARKET",
                    currencyCode,
                    "FILL_OR_KILL",
                    ForceOpen: true,
                    GuaranteedStop: false,
                    dealReference),
                cancellationToken);

            var summary = await GetOrderStatusAsync(dealReference, cancellationToken);
            return new PlaceOrderResult(
                dealReference,
                summary?.DealId,
                summary?.Status ?? OrderStatus.Pending,
                summary?.Message,
                summary?.TimestampUtc ?? DateTimeOffset.UtcNow);
        }
        catch (IgApiException exception)
        {
            throw TranslateException(exception);
        }
    }

    public async Task<ClosePositionResult> ClosePositionAsync(
        Trading.Abstractions.ClosePositionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DealId))
        {
            throw new ArgumentException("DealId is required.", nameof(request));
        }

        try
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

            var dealReference = $"spike-close-{Guid.NewGuid():N}";
            await _igTradingApi.ClosePositionAsync(
                new SdkClosePositionRequest(
                    request.DealId,
                    ToOppositeDirection(position.Position.Direction),
                    closeSize,
                    "MARKET",
                    "FILL_OR_KILL",
                    dealReference),
                cancellationToken);

            var status = await GetOrderStatusAsync(dealReference, cancellationToken);
            return new ClosePositionResult(
                dealReference,
                status?.DealId,
                status?.Status ?? OrderStatus.Pending,
                status?.Message,
                status?.TimestampUtc ?? DateTimeOffset.UtcNow);
        }
        catch (IgApiException exception)
        {
            throw TranslateException(exception);
        }
    }

    public async Task<IReadOnlyList<PositionSummary>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _igTradingApi.GetOpenPositionsAsync(cancellationToken);
            return (response.Positions ?? [])
                .Select(MapPosition)
                .ToList();
        }
        catch (IgApiException exception)
        {
            throw TranslateException(exception);
        }
    }

    public async Task<IReadOnlyList<OrderSummary>> GetOrdersAsync(
        OrderQuery query,
        CancellationToken cancellationToken = default)
    {
        query.Validate();

        try
        {
            var response = await _igTradingApi.GetActivityAsync(query.FromUtc, query.ToUtc, query.MaxItems, cancellationToken);

            return (response.Activities ?? [])
                .Where(activity => !string.IsNullOrWhiteSpace(activity.DealReference) || !string.IsNullOrWhiteSpace(activity.DealId))
                .Select(MapActivity)
                .ToList();
        }
        catch (IgApiException exception)
        {
            throw TranslateException(exception);
        }
    }

    public async Task<OrderSummary?> GetOrderStatusAsync(
        string dealReference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dealReference))
        {
            throw new ArgumentException("Deal reference is required.", nameof(dealReference));
        }

        try
        {
            var confirmation = await _igTradingApi.GetDealConfirmationAsync(dealReference, cancellationToken);
            if (confirmation is not null)
            {
                return MapConfirmation(confirmation, dealReference);
            }

            var now = DateTimeOffset.UtcNow;
            var activities = await _igTradingApi.GetActivityAsync(now.AddHours(-24), now, 200, cancellationToken);
            var activityMatch = (activities.Activities ?? [])
                .FirstOrDefault(x => string.Equals(x.DealReference, dealReference, StringComparison.OrdinalIgnoreCase));

            if (activityMatch is not null)
            {
                return MapActivity(activityMatch) with { DealReference = dealReference };
            }

            _logger.LogInformation("No confirm/activity found for deal reference {DealReference}; returning pending.", dealReference);
            return new OrderSummary(dealReference, null, null, null, null, OrderStatus.Pending, "Awaiting broker confirmation.", DateTimeOffset.UtcNow);
        }
        catch (IgApiException exception)
        {
            throw TranslateException(exception);
        }
    }

    private static PositionSummary MapPosition(PositionEnvelope source)
    {
        return new PositionSummary(
            source.Position.DealId,
            new InstrumentId(source.Market.Epic),
            ParseDirection(source.Position.Direction),
            source.Position.Size,
            source.Position.Currency,
            ParseDate(source.Position.CreatedDateUtc));
    }

    private static OrderSummary MapConfirmation(DealConfirmationResponse source, string fallbackDealReference)
    {
        return new OrderSummary(
            source.DealReference ?? fallbackDealReference,
            source.DealId,
            source.Epic is null ? null : new InstrumentId(source.Epic),
            source.Direction is null ? null : ParseDirection(source.Direction),
            source.Size,
            MapOrderStatus(source.DealStatus, source.Status),
            source.Reason,
            ParseDate(source.Date));
    }

    private static OrderSummary MapActivity(ActivityItem activity)
    {
        var actionType = activity.Details?.Actions?.FirstOrDefault()?.ActionType;
        var status = MapActivityStatus(activity.Details?.Status, actionType);

        return new OrderSummary(
            activity.DealReference ?? activity.DealId ?? "unknown",
            activity.DealId,
            activity.Epic is null ? null : new InstrumentId(activity.Epic),
            activity.Details?.Direction is null ? null : ParseDirection(activity.Details.Direction),
            activity.Details?.Size,
            status,
            activity.Details?.Status,
            ParseDate(activity.DateUtc));
    }

    private static OrderStatus MapOrderStatus(string? dealStatus, string? status)
    {
        if (string.Equals(dealStatus, "REJECTED", StringComparison.OrdinalIgnoreCase))
        {
            return OrderStatus.Rejected;
        }

        if (string.Equals(dealStatus, "ACCEPTED", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(status, "OPEN", StringComparison.OrdinalIgnoreCase))
            {
                return OrderStatus.Open;
            }

            if (string.Equals(status, "CLOSED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "DELETED", StringComparison.OrdinalIgnoreCase))
            {
                return OrderStatus.Closed;
            }

            return OrderStatus.Accepted;
        }

        return OrderStatus.Unknown;
    }

    private static OrderStatus MapActivityStatus(string? status, string? actionType)
    {
        if (string.Equals(status, "REJECTED", StringComparison.OrdinalIgnoreCase))
        {
            return OrderStatus.Rejected;
        }

        if (string.Equals(actionType, "POSITION_CLOSED", StringComparison.OrdinalIgnoreCase))
        {
            return OrderStatus.Closed;
        }

        if (string.Equals(actionType, "POSITION_OPENED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actionType, "POSITION_PARTIALLY_CLOSED", StringComparison.OrdinalIgnoreCase))
        {
            return OrderStatus.Open;
        }

        if (string.Equals(status, "ACCEPTED", StringComparison.OrdinalIgnoreCase))
        {
            return OrderStatus.Accepted;
        }

        return OrderStatus.Unknown;
    }

    private static string ResolveCurrencyCode(MarketDetailsResponse market)
    {
        var currency = market.Instrument.Currencies?.FirstOrDefault(x => x.IsDefault)
                       ?? market.Instrument.Currencies?.FirstOrDefault();

        if (currency is null || string.IsNullOrWhiteSpace(currency.Code))
        {
            throw new TradingGatewayException(TradingErrorCode.InvalidRequest, "Unable to determine default market currency.");
        }

        return currency.Code;
    }

    private static void EnsureMarketIsTradable(MarketDetailsResponse market)
    {
        if (!string.Equals(market.Snapshot.MarketStatus, "TRADEABLE", StringComparison.OrdinalIgnoreCase))
        {
            throw new TradingGatewayException(TradingErrorCode.MarketClosed, $"Market is not tradeable. Status: {market.Snapshot.MarketStatus}.");
        }
    }

    private static string ToIgDirection(TradeDirection direction)
        => direction switch
        {
            TradeDirection.Buy => "BUY",
            TradeDirection.Sell => "SELL",
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unsupported trade direction."),
        };

    private static TradeDirection ParseDirection(string direction)
        => string.Equals(direction, "SELL", StringComparison.OrdinalIgnoreCase)
            ? TradeDirection.Sell
            : TradeDirection.Buy;

    private static string ToOppositeDirection(string direction)
        => string.Equals(direction, "BUY", StringComparison.OrdinalIgnoreCase)
            ? "SELL"
            : "BUY";

    private static DateTimeOffset ParseDate(string? value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.UtcNow;

    private static TradingGatewayException TranslateException(IgApiException exception)
    {
        var code = MapErrorCode(exception.ErrorCode);
        return new TradingGatewayException(code, exception.Message, exception);
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
