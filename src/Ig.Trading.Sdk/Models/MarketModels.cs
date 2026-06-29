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
    [property: JsonPropertyName("currencies")] IReadOnlyList<MarketCurrency>? Currencies,
    [property: JsonPropertyName("lotSize")]
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    decimal? LotSize = null,
    [property: JsonPropertyName("unit")] string? Unit = null,
    [property: JsonPropertyName("forceOpenAllowed")] bool? ForceOpenAllowed = null,
    [property: JsonPropertyName("stopsLimitsAllowed")] bool? StopsLimitsAllowed = null,
    [property: JsonPropertyName("controlledRiskAllowed")] bool? ControlledRiskAllowed = null,
    [property: JsonPropertyName("streamingPricesAvailable")] bool? StreamingPricesAvailable = null);

public sealed record MarketCurrency(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("isDefault")] bool IsDefault);

public sealed record MarketSnapshot(
    [property: JsonPropertyName("marketStatus")] string MarketStatus,
    [property: JsonPropertyName("bid")] decimal? Bid,
    [property: JsonPropertyName("offer")] decimal? Offer,
    [property: JsonPropertyName("priceLadder")] IReadOnlyList<MarketPriceLadderLevel>? PriceLadder = null);

public sealed record MarketPriceLadderLevel(
    [property: JsonPropertyName("bid")]
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    decimal? Bid,
    [property: JsonPropertyName("ask")]
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    decimal? Ask);

public sealed record MarketDealingRules(
    [property: JsonPropertyName("minNormalStopOrLimitDistance")] MarketRuleDistance? MinNormalStopOrLimitDistance,
    [property: JsonPropertyName("minDealSize")] MarketRuleDistance? MinDealSize = null,
    [property: JsonPropertyName("minControlledRiskStopDistance")] MarketRuleDistance? MinControlledRiskStopDistance = null,
    [property: JsonPropertyName("minStepDistance")] MarketRuleDistance? MinStepDistance = null,
    [property: JsonPropertyName("maxStopOrLimitDistance")] MarketRuleDistance? MaxStopOrLimitDistance = null,
    [property: JsonPropertyName("marketOrderPreference")] string? MarketOrderPreference = null,
    [property: JsonPropertyName("trailingStopsPreference")] string? TrailingStopsPreference = null);

public sealed record MarketRuleDistance(
    [property: JsonPropertyName("value")] decimal? Value,
    [property: JsonPropertyName("unit")] string? Unit);
