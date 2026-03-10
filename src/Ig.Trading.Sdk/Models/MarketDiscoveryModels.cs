using System.Text.Json.Serialization;

namespace Ig.Trading.Sdk.Models;

public sealed record MarketSearchResponse(
    [property: JsonPropertyName("markets")] IReadOnlyList<MarketSearchItem>? Markets);

public sealed record MarketSearchItem(
    [property: JsonPropertyName("instrumentName")] string? InstrumentName,
    [property: JsonPropertyName("epic")] string Epic,
    [property: JsonPropertyName("expiry")] string? Expiry,
    [property: JsonPropertyName("instrumentType")] string? InstrumentType,
    [property: JsonPropertyName("marketStatus")] string? MarketStatus,
    [property: JsonPropertyName("currency")] string? CurrencyCode);

public sealed record MarketNavigationResponse(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("nodes")] IReadOnlyList<MarketNavigationNodeItem>? Nodes,
    [property: JsonPropertyName("markets")] IReadOnlyList<MarketSearchItem>? Markets);

public sealed record MarketNavigationNodeItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);
