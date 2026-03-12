namespace Trading.AI.Observability;

public sealed class PromptObservationRecord
{
    public required string PromptId { get; init; }

    public required string PromptName { get; init; }

    public required string Status { get; set; }

    public required DateTimeOffset RequestedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public required DateOnly PromptDate { get; init; }

    public required string ModelId { get; init; }

    public required string RequestText { get; init; }

    public object? RequestOptions { get; init; }

    public string? ResponseText { get; set; }

    public object? StructuredResponse { get; set; }

    public object? Usage { get; set; }

    public CostBreakdown? Cost { get; set; }

    public object? RawResponse { get; set; }

    public string? Error { get; set; }

    public double? DurationMs { get; set; }

    public string? TextArtifactPath { get; set; }

    public string? StructuredArtifactPath { get; set; }

    public IReadOnlyList<string>? AttachmentArtifactPaths { get; set; }
}

public sealed record CostBreakdown(
    int InputTokens,
    int OutputTokens,
    int CachedInputTokens,
    decimal InputCostUsd,
    decimal OutputCostUsd,
    decimal CachedInputCostUsd,
    decimal TotalCostUsd);
