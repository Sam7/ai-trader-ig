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

    public static OrderSummary? CorrelateFromSubmission(
        OrderSubmissionRecord submission,
        IReadOnlyList<ActivityItem> activities)
    {
        if (submission.Kind != OrderSubmissionKind.Close || string.IsNullOrWhiteSpace(submission.RelatedDealId))
        {
            return null;
        }

        var match = activities
            .Where(activity => string.Equals(
                activity.Details?.Actions?.FirstOrDefault()?.AffectedDealId,
                submission.RelatedDealId,
                StringComparison.OrdinalIgnoreCase))
            .Where(activity => activity.Details?.Size == submission.Size)
            .Where(activity => activity.Details?.Direction is not null
                && IgTradingConversions.ParseDirection(activity.Details.Direction) == submission.Direction)
            .Where(activity =>
            {
                var timestamp = IgTradingConversions.ParseDate(activity.DateUtc ?? activity.Date);
                return timestamp >= submission.SubmittedAtUtc.AddDays(-1)
                    && timestamp <= submission.SubmittedAtUtc.AddDays(1);
            })
            .OrderBy(activity => Math.Abs((IgTradingConversions.ParseDate(activity.DateUtc ?? activity.Date) - submission.SubmittedAtUtc).Ticks))
            .FirstOrDefault();

        if (match is null)
        {
            return null;
        }

        return MapActivity(match) with
        {
            DealReference = submission.DealReference,
        };
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
                IgTradingConversions.ParseDate(price.SnapshotTimeUtc),
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
            bars);
    }

    public static string? ResolveActivityDealReference(ActivityItem activity)
        => activity.Details?.DealReference ?? activity.DealReference;

    public static string? ResolveTransactionReference(TransactionItem transaction)
        => string.IsNullOrWhiteSpace(transaction.Reference) ? null : $"DIAAAAW{transaction.Reference}";

    private static string? ResolveDealIdFromReference(string? reference)
        => string.IsNullOrWhiteSpace(reference) ? null : $"DIAAAAW{reference}";

    private static decimal? ParseSignedDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value, out var parsed) ? parsed : null;
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
