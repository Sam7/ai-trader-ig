using Ig.Trading.Sdk.Models;
using Trading.Abstractions;

namespace Trading.IG;

internal static class IgTradingMapper
{
    public static PositionSummary MapPosition(PositionEnvelope source)
    {
        return new PositionSummary(
            source.Position.DealId,
            new InstrumentId(source.Market.Epic),
            IgTradingConversions.ParseDirection(source.Position.Direction),
            source.Position.Size,
            source.Position.Currency,
            IgTradingConversions.ParseDate(source.Position.CreatedDateUtc),
            source.Position.StopLevel,
            source.Position.LimitLevel,
            source.Position.TrailingStopDistance,
            source.Position.TrailingStopIncrement);
    }

    public static WorkingOrderSummary MapWorkingOrder(WorkingOrderEnvelope source)
    {
        return new WorkingOrderSummary(
            source.WorkingOrderData.DealId,
            new InstrumentId(source.MarketData.Epic),
            IgTradingConversions.ParseDirection(source.WorkingOrderData.Direction),
            IgTradingConversions.ParseWorkingOrderType(source.WorkingOrderData.OrderType),
            source.WorkingOrderData.OrderSize,
            source.WorkingOrderData.OrderLevel,
            IgTradingConversions.ParseTimeInForce(source.WorkingOrderData.TimeInForce),
            IgTradingConversions.ParseNullableDate(source.WorkingOrderData.GoodTillDateIso ?? source.WorkingOrderData.GoodTillDate),
            OrderStatus.Pending,
            source.WorkingOrderData.CurrencyCode,
            IgTradingConversions.ParseDate(source.WorkingOrderData.CreatedDateUtc));
    }

    public static OrderSummary MapConfirmation(DealConfirmationResponse source, string fallbackDealReference)
    {
        return new OrderSummary(
            source.DealReference ?? fallbackDealReference,
            source.DealId,
            source.Epic is null ? null : new InstrumentId(source.Epic),
            source.Direction is null ? null : IgTradingConversions.ParseDirection(source.Direction),
            source.Size,
            MapOrderStatus(source.DealStatus, source.Status),
            source.Reason,
            IgTradingConversions.ParseDate(source.Date));
    }

    public static OrderSummary MapActivity(ActivityItem activity)
    {
        var actionType = activity.Details?.Actions?.FirstOrDefault()?.ActionType;
        var status = MapActivityStatus(activity.Status ?? activity.Details?.Status, actionType);
        var dealReference = ResolveActivityDealReference(activity);

        return new OrderSummary(
            dealReference ?? activity.DealId ?? "unknown",
            activity.DealId,
            activity.Epic is null ? null : new InstrumentId(activity.Epic),
            activity.Details?.Direction is null ? null : IgTradingConversions.ParseDirection(activity.Details.Direction),
            activity.Details?.Size,
            status,
            activity.Description ?? activity.Status ?? activity.Details?.Status,
            IgTradingConversions.ParseDate(activity.DateUtc ?? activity.Date));
    }

    public static OrderSummary MapTransaction(TransactionItem transaction, string fallbackDealReference)
    {
        var dealReference = ResolveTransactionReference(transaction) ?? fallbackDealReference;
        var size = ParseSignedDecimal(transaction.Size);

        return new OrderSummary(
            dealReference,
            ResolveDealIdFromReference(transaction.Reference),
            null,
            size is null ? null : size.Value < 0 ? TradeDirection.Buy : TradeDirection.Sell,
            size is null ? null : decimal.Abs(size.Value),
            OrderStatus.Closed,
            transaction.ProfitAndLoss,
            IgTradingConversions.ParseDate(transaction.DateUtc ?? transaction.Date));
    }

    public static MarketSearchResult MapMarketSearchResult(MarketSearchItem source)
    {
        return new MarketSearchResult(
            new InstrumentId(source.Epic),
            source.InstrumentName ?? source.Epic,
            source.InstrumentType,
            source.Expiry,
            source.CurrencyCode,
            IgTradingConversions.ParseMarketStatus(source.MarketStatus));
    }

    public static MarketDetails MapMarketDetails(MarketDetailsResponse source)
    {
        return new MarketDetails(
            new InstrumentId(source.Instrument.Epic),
            source.Instrument.Name ?? source.Instrument.Epic,
            IgTradingConversions.ParseMarketStatus(source.Snapshot.MarketStatus),
            source.Instrument.Type,
            source.Instrument.Expiry,
            ResolveCurrencyCodeOrNull(source),
            ResolveBid(source.Snapshot),
            ResolveAsk(source.Snapshot),
            source.Instrument.LotSize,
            source.Instrument.Unit,
            source.Instrument.ForceOpenAllowed,
            source.Instrument.StopsLimitsAllowed,
            source.Instrument.ControlledRiskAllowed,
            source.Instrument.StreamingPricesAvailable,
            MapMarketDealingRules(source.DealingRules),
            ResolveSupportedOrderTypes(source));
    }

    public static MarketNavigationPage MapMarketNavigation(string? nodeId, MarketNavigationResponse source)
    {
        return new MarketNavigationPage(
            nodeId,
            source.Name ?? "Markets",
            (source.Nodes ?? [])
                .Select(node => new MarketNavigationNode(node.Id, node.Name))
                .ToList(),
            (source.Markets ?? [])
                .Select(MapMarketSearchResult)
                .ToList());
    }

    public static PriceSeries MapPrices(Trading.Abstractions.GetPricesRequest request, PricesResponse source)
    {
        var bars = (source.Prices ?? [])
            .Select(price => new PriceBar(
                price.TimestampUtc ?? throw new TradingGatewayException(
                    TradingErrorCode.BrokerError,
                    "IG returned a price bar without a normalized UTC timestamp."),
                price.OpenPrice?.Bid ?? 0m,
                price.HighPrice?.Bid ?? 0m,
                price.LowPrice?.Bid ?? 0m,
                price.ClosePrice?.Bid ?? 0m,
                price.OpenPrice?.Ask ?? 0m,
                price.HighPrice?.Ask ?? 0m,
                price.LowPrice?.Ask ?? 0m,
                price.ClosePrice?.Ask ?? 0m,
                price.LastTradedVolume))
            .ToList();

        return new PriceSeries(
            request.Instrument,
            request.Resolution,
            bars,
            source.Metadata?.Allowance is { } allowance
                ? new HistoricalPriceAllowance(
                    allowance.RemainingAllowance,
                    allowance.AllowanceExpirySeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null)
                : null);
    }

    public static string? ResolveActivityDealReference(ActivityItem activity)
        => activity.Details?.DealReference ?? activity.DealReference;

    public static string? ResolveTransactionReference(TransactionItem transaction)
        => string.IsNullOrWhiteSpace(transaction.Reference) ? null : $"DIAAAAW{transaction.Reference}";

    private static MarketDealingRulesSummary? MapMarketDealingRules(MarketDealingRules? source)
    {
        return source is null
            ? null
            : new MarketDealingRulesSummary(
                MapMarketRuleDistance(source.MinDealSize),
                MapMarketRuleDistance(source.MinStepDistance),
                MapMarketRuleDistance(source.MinControlledRiskStopDistance),
                MapMarketRuleDistance(source.MinNormalStopOrLimitDistance),
                MapMarketRuleDistance(source.MaxStopOrLimitDistance),
                source.MarketOrderPreference,
                source.TrailingStopsPreference);
    }

    private static MarketRuleDistanceSummary? MapMarketRuleDistance(MarketRuleDistance? source)
        => source is null ? null : new MarketRuleDistanceSummary(source.Value, source.Unit);

    private static decimal? ResolveBid(MarketSnapshot snapshot)
        => snapshot.Bid ?? snapshot.PriceLadder?.FirstOrDefault()?.Bid;

    private static decimal? ResolveAsk(MarketSnapshot snapshot)
        => snapshot.Offer ?? snapshot.PriceLadder?.FirstOrDefault()?.Ask;

    private static string? ResolveCurrencyCodeOrNull(MarketDetailsResponse market)
    {
        var currency = market.Instrument.Currencies?.FirstOrDefault(x => x.IsDefault)
                       ?? market.Instrument.Currencies?.FirstOrDefault();

        return string.IsNullOrWhiteSpace(currency?.Code) ? null : currency.Code;
    }

    private static IReadOnlyList<string> ResolveSupportedOrderTypes(MarketDetailsResponse market)
    {
        var orderTypes = new List<string>();
        if (!string.IsNullOrWhiteSpace(market.DealingRules?.MarketOrderPreference)
            && !market.DealingRules.MarketOrderPreference.Equals("NOT_AVAILABLE", StringComparison.OrdinalIgnoreCase))
        {
            orderTypes.Add("MARKET");
        }

        return orderTypes;
    }

    private static string? ResolveDealIdFromReference(string? reference)
        => string.IsNullOrWhiteSpace(reference) ? null : $"DIAAAAW{reference}";

    private static decimal? ParseSignedDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (decimal.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new TradingGatewayException(
            TradingErrorCode.BrokerError,
            $"IG returned an invalid decimal value '{value}'.");
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
}
