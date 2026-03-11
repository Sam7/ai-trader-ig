namespace Trading.AI.Configuration;

public sealed class ModelPricingOptions
{
    public decimal InputUsdPerMillionTokens { get; init; }

    public decimal OutputUsdPerMillionTokens { get; init; }

    public decimal CachedInputUsdPerMillionTokens { get; init; }
}
