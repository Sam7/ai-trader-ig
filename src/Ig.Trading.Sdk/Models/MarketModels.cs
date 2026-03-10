using System.Text.Json.Serialization;

namespace Ig.Trading.Sdk.Models;

public sealed record MarketDetailsResponse(
    [property: JsonPropertyName("instrument")] MarketInstrument Instrument,
    [property: JsonPropertyName("snapshot")] MarketSnapshot Snapshot,
    [property: JsonPropertyName("dealingRules")] MarketDealingRules? DealingRules);

public sealed record MarketInstrument(
    [property: JsonPropertyName("epic")] string Epic,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("expiry")] string Expiry,
    [property: JsonPropertyName("currencies")] IReadOnlyList<MarketCurrency>? Currencies);

public sealed record MarketCurrency(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("isDefault")] bool IsDefault);

public sealed record MarketSnapshot(
    [property: JsonPropertyName("marketStatus")] string MarketStatus,
    [property: JsonPropertyName("bid")] decimal? Bid,
    [property: JsonPropertyName("offer")] decimal? Offer);

public sealed record MarketDealingRules(
    [property: JsonPropertyName("minNormalStopOrLimitDistance")] MarketRuleDistance? MinNormalStopOrLimitDistance);

public sealed record MarketRuleDistance(
    [property: JsonPropertyName("value")] decimal? Value,
    [property: JsonPropertyName("unit")] string? Unit);
