namespace Trading.AI.Configuration;

public sealed class DailyBriefingModelOptions
{
    public string ModelId { get; init; } = string.Empty;

    public decimal? Temperature { get; init; }

    public int? MaxOutputTokens { get; init; }

    public bool EnableWebSearch { get; init; }

    public ModelPricingOptions Pricing { get; init; } = new();
}
