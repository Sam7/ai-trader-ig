namespace Trading.AI.Configuration;

public class PromptModelOptions
{
    public string ModelId { get; set; } = string.Empty;

    public decimal? Temperature { get; set; }

    public int? MaxOutputTokens { get; set; }

    public bool EnableWebSearch { get; set; }

    public ModelPricingOptions Pricing { get; set; } = new();
}
