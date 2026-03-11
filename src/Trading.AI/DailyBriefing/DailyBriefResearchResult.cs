namespace Trading.AI.DailyBriefing;

public sealed record DailyBriefResearchResult(
    string Markdown,
    string ArtifactPath,
    DateTimeOffset CompletedAtUtc);
