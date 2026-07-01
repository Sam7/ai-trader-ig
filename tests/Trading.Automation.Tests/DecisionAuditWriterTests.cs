using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.Prompts.IntradayOpportunityReview;
using Trading.Abstractions;
using Trading.Automation.Execution;
using Trading.Strategy.Shared;

public sealed class DecisionAuditWriterTests
{
    [Fact]
    public async Task WriteInitialAsync_ShouldPersistPaperOnlyAuditWithPromptMetadata()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var writer = new DecisionAuditWriter(
                Options.Create(new PromptObservabilityOptions
                {
                    ObservabilityRootPath = tempDirectory.FullName,
                }));
            var envelopePath = Path.Combine(tempDirectory.FullName, "prompt-envelope.json");
            await File.WriteAllTextAsync(
                envelopePath,
                """
                {
                  "modelId": "gpt-test",
                  "processingMode": "ResponsesBackground",
                  "providerResponseId": "resp_test",
                  "providerStatus": "completed"
                }
                """);
            var extractedPath = Path.Combine(tempDirectory.FullName, "prompt-extracted.json");
            await File.WriteAllTextAsync(extractedPath, "{}");
            var prepared = CreatePreparation(tempDirectory.FullName);
            var result = CreateReviewResult();

            var artifact = await writer.WriteInitialAsync(
                prepared,
                new IntradayOpportunityExecutionArtifacts(
                    ToArtifact(envelopePath),
                    ToArtifact(extractedPath),
                    []),
                result,
                CancellationToken.None);

            File.Exists(artifact.Path).Should().BeTrue();
            var record = JsonSerializer.Deserialize<DecisionAuditRecord>(
                await File.ReadAllTextAsync(artifact.Path),
                DecisionAuditJson.Options);
            record.Should().NotBeNull();
            record!.Decision.Should().Be(DecisionAuditDecision.PaperOnly);
            record.Prompt.ProviderResponseId.Should().Be("resp_test");
            record.Prompt.ProcessingMode.Should().Be("ResponsesBackground");
            record.MarketAssessments.Should().ContainSingle();
            record.CandidateOpportunities.Should().ContainSingle();
            record.PaperOutcomes.Should().ContainSingle();
            record.PaperOutcomes[0].Status.Should().Be(PaperTradeOutcomeStatus.DataInsufficient);
            record.MarketAssessmentOutcomes.Should().ContainSingle();
            record.MarketAssessmentOutcomes[0].Status.Should().Be(PaperMarketAssessmentOutcomeStatus.DataInsufficient);
            record.BiasSummary.DominantCandidateDirection.Should().Be("Buy");
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    private static IntradayOpportunityPreparationDocument CreatePreparation(string rootPath)
    {
        var preparedPath = Path.Combine(rootPath, "prepared.json");
        var requestPath = Path.Combine(rootPath, "request.txt");
        File.WriteAllText(preparedPath, "{}");
        File.WriteAllText(requestPath, "request");

        return new IntradayOpportunityPreparationDocument(
            new DateOnly(2026, 3, 12),
            DateTimeOffset.Parse("2026-03-12T10:00:00Z"),
            "intraday-opportunity-review",
            new IntradayOpportunityReviewInput(
                new DateOnly(2026, 3, 12),
                DateTimeOffset.Parse("2026-03-12T09:00:00Z"),
                DateTimeOffset.Parse("2026-03-12T10:00:00Z"),
                1,
                4,
                "Australia/Melbourne",
                "Daily plan",
                "Watched markets",
                "No events",
                new DateOnly(2026, 3, 12),
                DateTimeOffset.Parse("2026-03-12T10:00:00Z")),
            "request",
            [],
            [],
            ToArtifact(preparedPath),
            ToArtifact(requestPath));
    }

    private static IntradayOpportunityReviewResult CreateReviewResult()
        => new(
            new DateOnly(2026, 3, 12),
            [
                new IntradayMarketAssessment(
                    new InstrumentId("CC.D.TEST.IP"),
                    "Test Market",
                    72,
                    TradeDirection.Buy,
                    "Constructive",
                    "Momentum improved",
                    "")
            ],
            [
                new IntradayOpportunityCandidate(
                    new InstrumentId("CC.D.TEST.IP"),
                    "Test Market",
                    TradeDirection.Buy,
                    72,
                    TradeEntryMethod.Market,
                    100m,
                    95m,
                    110m,
                    2m,
                    100m,
                    0.2m,
                    "Thesis",
                    "Invalidation",
                    "Why now",
                    DateTimeOffset.Parse("2026-03-12T10:30:00Z"))
            ],
            DateTimeOffset.Parse("2026-03-12T10:01:00Z"),
            "Validated intraday opportunity batch. Decision logic pending.");

    private static ArtifactReference ToArtifact(string path)
        => new(Path.GetFullPath(path), new Uri(Path.GetFullPath(path)).AbsoluteUri);
}
