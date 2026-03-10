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
    [property: JsonPropertyName("dealDirection")] string DealDirection,
    [property: JsonPropertyName("size")] decimal Size,
    [property: JsonPropertyName("createdDateUTC")] string? CreatedDateUtc);

public sealed record WorkingOrderMarketData(
    [property: JsonPropertyName("epic")] string Epic,
    [property: JsonPropertyName("expiry")] string Expiry);

public sealed record ActivityResponse(
    [property: JsonPropertyName("activities")] IReadOnlyList<ActivityItem>? Activities);

public sealed record ActivityItem(
    [property: JsonPropertyName("dateUtc")] string? DateUtc,
    [property: JsonPropertyName("details")] ActivityDetails? Details,
    [property: JsonPropertyName("dealId")] string? DealId,
    [property: JsonPropertyName("epic")] string? Epic,
    [property: JsonPropertyName("dealReference")] string? DealReference);

public sealed record ActivityDetails(
    [property: JsonPropertyName("actions")] IReadOnlyList<ActivityAction>? Actions,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("direction")] string? Direction,
    [property: JsonPropertyName("size")] decimal? Size,
    [property: JsonPropertyName("level")] decimal? Level,
    [property: JsonPropertyName("currency")] string? Currency);

public sealed record ActivityAction(
    [property: JsonPropertyName("actionType")] string? ActionType,
    [property: JsonPropertyName("affectedDealId")] string? AffectedDealId);
