namespace Trading.AI.Configuration;

public sealed class TrackedMarketOptions
{
    public string DisplayName { get; init; } = string.Empty;

    public string InstrumentId { get; init; } = string.Empty;

    public string Sector { get; init; } = string.Empty;

    public string[] Aliases { get; init; } = [];
}
