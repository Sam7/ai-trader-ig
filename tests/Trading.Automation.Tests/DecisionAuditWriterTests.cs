using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.Prompts.IntradayOpportunityReview;
using Trading.Abstractions;
using Trading.Automation.Execution;
using Trading.Execution;
using Trading.Strategy.Shared;

public sealed class DecisionAuditWriterTests
{
    [Fact]
    public async Task WriteInitialAsync_ShouldPersistShadowDecisionAuditWithPromptMetadata()
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
            var executionBoundary = new ExecutionBoundarySnapshot(
                "dec_test",
                ExecutionBoundaryState.Reserved,
                "ATOPEN123",
                null,
                0,
                DateTimeOffset.Parse("2026-03-12T10:01:00Z"),
                DateTimeOffset.Parse("2026-03-12T10:01:00Z"),
                null);

            var artifact = await writer.WriteInitialAsync(
                prepared,
                new IntradayOpportunityExecutionArtifacts(
                    ToArtifact(envelopePath),
                    ToArtifact(extractedPath),
                    []),
                result,
                executionBoundary,
                CancellationToken.None);

            File.Exists(artifact.Path).Should().BeTrue();
            var record = JsonSerializer.Deserialize<DecisionAuditRecord>(
                await File.ReadAllTextAsync(artifact.Path),
                DecisionAuditJson.Options);
            record.Should().NotBeNull();
            record!.AuditId.Should().Be("2026-03-12/100000000-decision-audit");
            record.Decision.Should().Be(DecisionAuditDecision.ShadowRejected);
            record.Prompt.ProviderResponseId.Should().Be("resp_test");
            record.Prompt.ProcessingMode.Should().Be("ResponsesBackground");
            record.MarketAssessments.Should().ContainSingle();
            record.CandidateOpportunities.Should().ContainSingle();
            record.ExecutionMode.Should().Be(TradingExecutionMode.Disabled);
            record.ShadowDecisions.Should().ContainSingle();
            record.ShadowDecisions[0].Reasons.Should().Contain(IntradayCandidateDecisionReason.ExecutionDisabled);
            record.SelectedShadowIntent.Should().BeNull();
            record.ExecutionBoundary.Should().Be(executionBoundary);
            record.DecisionSummary.Rejected.Should().Be(1);
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

    [Fact]
    public async Task LoadAsync_WithLegacyAuditWithoutShadowFields_ShouldNormalizeDefaults()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var dayPath = Directory.CreateDirectory(Path.Combine(tempDirectory.FullName, "2026-03-12"));
            var auditPath = Path.Combine(dayPath.FullName, "100000000-decision-audit.json");
            await File.WriteAllTextAsync(
                auditPath,
                """
                {
                  "tradingDate": "2026-03-12",
                  "reviewedAtUtc": "2026-03-12T10:00:00+00:00",
                  "generatedAtUtc": "2026-03-12T10:01:00+00:00",
                  "decision": "NoCandidate",
                  "outcome": "No actionable candidates were returned.",
                  "prompt": {
                    "preparedArtifact": { "path": "prepared.json", "uri": "file:///prepared.json" },
                    "requestTextArtifact": { "path": "request.txt", "uri": "file:///request.txt" },
                    "promptEnvelopeArtifact": { "path": "envelope.json", "uri": "file:///envelope.json" },
                    "extractedJsonArtifact": { "path": "extracted.json", "uri": "file:///extracted.json" },
                    "modelId": "gpt-test",
                    "processingMode": "ResponsesBackground",
                    "providerResponseId": "resp_test",
                    "providerStatus": "completed"
                  },
                  "marketAssessments": [],
                  "candidateOpportunities": [],
                  "paperOutcomes": [],
                  "marketAssessmentOutcomes": [],
                  "biasSummary": {
                    "assessmentCount": 0,
                    "candidateCount": 0,
                    "buyAssessmentCount": 0,
                    "sellAssessmentCount": 0,
                    "buyCandidateCount": 0,
                    "sellCandidateCount": 0,
                    "dominantAssessmentDirection": "None",
                    "dominantCandidateDirection": "None",
                    "candidateCountByInstrument": {}
                  }
                }
                """);
            var writer = new DecisionAuditWriter(
                Options.Create(new PromptObservabilityOptions
                {
                    ObservabilityRootPath = tempDirectory.FullName,
                }));

            var record = await writer.LoadAsync(auditPath, CancellationToken.None);

            record.AuditId.Should().Be("2026-03-12/100000000-decision-audit");
            record.ExecutionMode.Should().Be(TradingExecutionMode.Disabled);
            record.ShadowDecisions.Should().BeEmpty();
            record.DecisionSummary.Considered.Should().Be(0);
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
    {
        var candidate = new IntradayOpportunityCandidate(
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
            DateTimeOffset.Parse("2026-03-12T10:30:00Z"));

        var decision = new IntradayCandidateDecision(
            "dec_test",
            candidate.Instrument,
            candidate.Direction,
            candidate.EntryMethod,
            candidate.OpportunityScore,
            IntradayCandidateDecisionStatus.Rejected,
            [IntradayCandidateDecisionReason.ExecutionDisabled],
            2m,
            0.04m,
            0m,
            "Execution mode is Disabled.",
            null);

        return new IntradayOpportunityReviewResult(
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
            [candidate],
            TradingExecutionMode.Disabled,
            [decision],
            null,
            new IntradayCandidateDecisionSummary(1, 0, 1, 0, 0),
            DateTimeOffset.Parse("2026-03-12T10:01:00Z"),
            "Validated intraday opportunity batch. Execution mode is Disabled.");
    }

    private static ArtifactReference ToArtifact(string path)
        => new(Path.GetFullPath(path), new Uri(Path.GetFullPath(path)).AbsoluteUri);
}
