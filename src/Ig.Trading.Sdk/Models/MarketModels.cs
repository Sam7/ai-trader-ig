using System.Text.Json.Serialization;

namespace Ig.Trading.Sdk.Models;

public sealed record MarketDetailsResponse(
    [property: JsonPropertyName("instrument")] MarketInstrument Instrument,
    [property: JsonPropertyName("snapshot")] MarketSnapshot Snapshot);

public sealed record MarketInstrument(
    [property: JsonPropertyName("epic")] string Epic,
    [property: JsonPropertyName("expiry")] string Expiry,
    [property: JsonPropertyName("currencies")] IReadOnlyList<MarketCurrency>? Currencies);

public sealed record MarketCurrency(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("isDefault")] bool IsDefault);

public sealed record MarketSnapshot(
    [property: JsonPropertyName("marketStatus")] string MarketStatus);
