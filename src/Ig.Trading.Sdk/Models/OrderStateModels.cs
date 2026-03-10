using System.Text.Json.Serialization;

namespace Ig.Trading.Sdk.Models;

public sealed record DealConfirmationResponse(
    [property: JsonPropertyName("dealStatus")] string DealStatus,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("dealId")] string? DealId,
    [property: JsonPropertyName("dealReference")] string? DealReference,
    [property: JsonPropertyName("epic")] string? Epic,
    [property: JsonPropertyName("direction")] string? Direction,
    [property: JsonPropertyName("size")] decimal? Size,
    [property: JsonPropertyName("date")] string? Date);

public sealed record WorkingOrdersResponse(
    [property: JsonPropertyName("workingOrders")] IReadOnlyList<WorkingOrderEnvelope>? WorkingOrders);

public sealed record WorkingOrderEnvelope(
    [property: JsonPropertyName("workingOrderData")] WorkingOrderData WorkingOrderData,
    [property: JsonPropertyName("marketData")] WorkingOrderMarketData MarketData);

public sealed record WorkingOrderData(
    [property: JsonPropertyName("dealId")] string DealId,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("epic")] string Epic,
    [property: JsonPropertyName("orderSize")] decimal OrderSize,
    [property: JsonPropertyName("orderLevel")] decimal OrderLevel,
    [property: JsonPropertyName("timeInForce")] string TimeInForce,
    [property: JsonPropertyName("goodTillDate")] string? GoodTillDate,
    [property: JsonPropertyName("goodTillDateISO")] string? GoodTillDateIso,
    [property: JsonPropertyName("createdDateUTC")] string? CreatedDateUtc,
    [property: JsonPropertyName("guaranteedStop")] bool GuaranteedStop,
    [property: JsonPropertyName("orderType")] string OrderType,
    [property: JsonPropertyName("stopDistance")] decimal? StopDistance,
    [property: JsonPropertyName("limitDistance")] decimal? LimitDistance,
    [property: JsonPropertyName("currencyCode")] string? CurrencyCode);

public sealed record WorkingOrderMarketData(
    [property: JsonPropertyName("instrumentName")] string? InstrumentName,
    [property: JsonPropertyName("epic")] string Epic,
    [property: JsonPropertyName("expiry")] string Expiry,
    [property: JsonPropertyName("marketStatus")] string? MarketStatus);

public sealed record ActivityResponse(
    [property: JsonPropertyName("activities")] IReadOnlyList<ActivityItem>? Activities);

public sealed record ActivityItem(
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("dateUtc")] string? DateUtc,
    [property: JsonPropertyName("details")] ActivityDetails? Details,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("dealId")] string? DealId,
    [property: JsonPropertyName("epic")] string? Epic,
    [property: JsonPropertyName("dealReference")] string? DealReference);

public sealed record ActivityDetails(
    [property: JsonPropertyName("dealReference")] string? DealReference,
    [property: JsonPropertyName("actions")] IReadOnlyList<ActivityAction>? Actions,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("direction")] string? Direction,
    [property: JsonPropertyName("size")] decimal? Size,
    [property: JsonPropertyName("level")] decimal? Level,
    [property: JsonPropertyName("currency")] string? Currency);

public sealed record ActivityAction(
    [property: JsonPropertyName("actionType")] string? ActionType,
    [property: JsonPropertyName("affectedDealId")] string? AffectedDealId);

public sealed record TransactionHistoryResponse(
    [property: JsonPropertyName("transactions")] IReadOnlyList<TransactionItem>? Transactions,
    [property: JsonPropertyName("metadata")] TransactionMetadata? Metadata);

public sealed record TransactionItem(
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("dateUtc")] string? DateUtc,
    [property: JsonPropertyName("openDateUtc")] string? OpenDateUtc,
    [property: JsonPropertyName("instrumentName")] string? InstrumentName,
    [property: JsonPropertyName("period")] string? Period,
    [property: JsonPropertyName("profitAndLoss")] string? ProfitAndLoss,
    [property: JsonPropertyName("transactionType")] string? TransactionType,
    [property: JsonPropertyName("reference")] string? Reference,
    [property: JsonPropertyName("openLevel")] string? OpenLevel,
    [property: JsonPropertyName("closeLevel")] string? CloseLevel,
    [property: JsonPropertyName("size")] string? Size,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("cashTransaction")] bool CashTransaction);

public sealed record TransactionMetadata(
    [property: JsonPropertyName("size")] int Size,
    [property: JsonPropertyName("pageData")] TransactionPageData? PageData);

public sealed record TransactionPageData(
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("pageNumber")] int PageNumber,
    [property: JsonPropertyName("totalPages")] int TotalPages);
