namespace Trading.AI.Configuration;

public sealed class ModelPricingOptions
{
    public decimal InputUsdPerMillionTokens { get; set; }

    public decimal OutputUsdPerMillionTokens { get; set; }

    public decimal CachedInputUsdPerMillionTokens { get; set; }
}
