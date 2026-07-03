using System.Text.Json.Serialization;

namespace Ig.Trading.Sdk.Models;

public sealed record CreatePositionRequest(
    [property: JsonPropertyName("epic")] string Epic,
    [property: JsonPropertyName("expiry")] string Expiry,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("size")] decimal Size,
    [property: JsonPropertyName("orderType")] string OrderType,
    [property: JsonPropertyName("currencyCode")] string CurrencyCode,
    [property: JsonPropertyName("timeInForce")] string TimeInForce,
    [property: JsonPropertyName("forceOpen")] bool ForceOpen,
    [property: JsonPropertyName("guaranteedStop")] bool GuaranteedStop,
    [property: JsonPropertyName("dealReference")] string DealReference,
    [property: JsonPropertyName("stopLevel")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? StopLevel = null,
    [property: JsonPropertyName("stopDistance")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? StopDistance = null,
    [property: JsonPropertyName("limitLevel")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? LimitLevel = null,
    [property: JsonPropertyName("limitDistance")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? LimitDistance = null,
    [property: JsonPropertyName("trailingStop")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? TrailingStop = null,
    [property: JsonPropertyName("trailingStopIncrement")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? TrailingStopIncrement = null);

public sealed record CreatePositionResponse(
    [property: JsonPropertyName("dealReference")] string DealReference);

public sealed record ClosePositionRequest(
    [property: JsonPropertyName("dealId")] string DealId,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("size")] decimal Size,
    [property: JsonPropertyName("orderType")] string OrderType,
    [property: JsonPropertyName("timeInForce")] string TimeInForce,
    [property: JsonPropertyName("dealReference")] string DealReference);

public sealed record ClosePositionResponse(
    [property: JsonPropertyName("dealReference")] string DealReference);

public sealed record UpdatePositionRequest(
    [property: JsonPropertyName("limitLevel")] decimal? LimitLevel,
    [property: JsonPropertyName("stopLevel")] decimal? StopLevel,
    [property: JsonPropertyName("trailingStop")] bool TrailingStop,
    [property: JsonPropertyName("trailingStopDistance")] decimal? TrailingStopDistance,
    [property: JsonPropertyName("trailingStopIncrement")] decimal? TrailingStopIncrement);

public sealed record UpdatePositionResponse(
    [property: JsonPropertyName("dealReference")] string DealReference);

public sealed record PositionsResponse(
    [property: JsonPropertyName("positions")] IReadOnlyList<PositionEnvelope>? Positions);

public sealed record PositionEnvelope(
    [property: JsonPropertyName("position")] PositionData Position,
    [property: JsonPropertyName("market")] PositionMarketData Market);

public sealed record PositionData(
    [property: JsonPropertyName("dealId")] string DealId,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("size")] decimal Size,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("level")] decimal? Level,
    [property: JsonPropertyName("createdDateUTC")] string? CreatedDateUtc,
    [property: JsonPropertyName("limitLevel")] decimal? LimitLevel,
    [property: JsonPropertyName("stopLevel")] decimal? StopLevel,
    [property: JsonPropertyName("trailingStopDistance")] decimal? TrailingStopDistance,
    [property: JsonPropertyName("trailingStep")] decimal? TrailingStopIncrement);

public sealed record PositionMarketData(
    [property: JsonPropertyName("epic")] string Epic,
    [property: JsonPropertyName("expiry")] string Expiry);
