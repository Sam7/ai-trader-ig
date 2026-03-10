using System.Text.Json.Serialization;

namespace Ig.Trading.Sdk.Models;

public sealed record CreateWorkingOrderRequest(
    [property: JsonPropertyName("epic")] string Epic,
    [property: JsonPropertyName("expiry")] string Expiry,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("size")] decimal Size,
    [property: JsonPropertyName("level")] decimal Level,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("currencyCode")] string CurrencyCode,
    [property: JsonPropertyName("guaranteedStop")] bool GuaranteedStop,
    [property: JsonPropertyName("timeInForce")] string TimeInForce,
    [property: JsonPropertyName("goodTillDate")] string? GoodTillDate = null);

public sealed record UpdateWorkingOrderRequest(
    [property: JsonPropertyName("level")] decimal Level,
    [property: JsonPropertyName("timeInForce")] string TimeInForce,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("goodTillDate")] string? GoodTillDate = null);

public sealed record WorkingOrderMutationResponse(
    [property: JsonPropertyName("dealReference")] string DealReference);
