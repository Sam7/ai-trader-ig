namespace Trading.Abstractions;

public sealed record MarketNavigationPage(
    string? CurrentNodeId,
    string Name,
    IReadOnlyList<MarketNavigationNode> Nodes,
    IReadOnlyList<MarketSearchResult> Markets);
