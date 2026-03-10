using System.Text.Json.Serialization;

namespace Ig.Trading.Sdk.Models;

public sealed record GetPricesRequest(
    string Epic,
    string? Resolution = null,
    int? MaxPoints = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null);

public sealed record PricesResponse(
    [property: JsonPropertyName("prices")] IReadOnlyList<PricePoint>? Prices,
    [property: JsonPropertyName("instrumentType")] string? InstrumentType,
    [property: JsonPropertyName("metadata")] PricesMetadata? Metadata);

public sealed record PricesMetadata(
    [property: JsonPropertyName("allowance")] PriceAllowance? Allowance,
    [property: JsonPropertyName("size")] int? Size);

public sealed record PriceAllowance(
    [property: JsonPropertyName("remainingAllowance")] int? RemainingAllowance,
    [property: JsonPropertyName("allowanceExpiry")] int? AllowanceExpirySeconds);

public sealed record PricePoint(
    [property: JsonPropertyName("snapshotTimeUTC")] string? SnapshotTimeUtc,
    [property: JsonPropertyName("openPrice")] PriceLevel? OpenPrice,
    [property: JsonPropertyName("highPrice")] PriceLevel? HighPrice,
    [property: JsonPropertyName("lowPrice")] PriceLevel? LowPrice,
    [property: JsonPropertyName("closePrice")] PriceLevel? ClosePrice,
    [property: JsonPropertyName("lastTradedVolume")] long? LastTradedVolume);

public sealed record PriceLevel(
    [property: JsonPropertyName("bid")] decimal? Bid,
    [property: JsonPropertyName("ask")] decimal? Ask);
