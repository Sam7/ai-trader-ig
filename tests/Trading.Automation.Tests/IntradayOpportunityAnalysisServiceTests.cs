using FluentAssertions;
using Trading.AI.DailyBriefing;
using Trading.AI.PromptExecution;
using Trading.AI.Prompts;
using Trading.Abstractions;
using Trading.Automation.Execution;
using Trading.Automation.Health;

public sealed class IntradayOpportunityAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_should_reject_an_unknown_preparation_schema()
    {
        var reviewer = new FakeReviewer("current request");
        var service = new IntradayOpportunityAnalysisService(reviewer, new WorkerOperationMetrics());
        var prepared = CreatePreparation("current request") with { SchemaVersion = "2" };

        var action = () => service.AnalyzeAsync(prepared);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unsupported preparation schema version*");
        reviewer.ReviewCalls.Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeAsync_should_reject_a_mismatched_prompt_identifier()
    {
        var reviewer = new FakeReviewer("current request");
        var service = new IntradayOpportunityAnalysisService(reviewer, new WorkerOperationMetrics());
        var prepared = CreatePreparation("current request") with { PromptId = "different-prompt" };

        var action = () => service.AnalyzeAsync(prepared);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not match prompt contract*");
        reviewer.ReviewCalls.Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeAsync_should_reject_a_preparation_rendered_by_a_different_prompt_contract()
    {
        var reviewer = new FakeReviewer("current request");
        var service = new IntradayOpportunityAnalysisService(reviewer, new WorkerOperationMetrics());
        var prepared = CreatePreparation("old request");

        var action = () => service.AnalyzeAsync(prepared);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no longer matches the current prompt template*");
        reviewer.ReviewCalls.Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeAsync_should_reject_tampered_evidence_before_calling_the_reviewer()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var evidencePath = Path.Combine(tempDirectory.FullName, "chart.png");
            await File.WriteAllBytesAsync(evidencePath, [9, 9, 9]);
            var artifact = new ArtifactReference(evidencePath, new Uri(evidencePath).AbsoluteUri);
            var evidence = new DecisionEvidence(
                "ev_test",
                DecisionEvidenceKind.PriceChart,
                "Test chart",
                null,
                "image/png",
                artifact,
                null,
                null,
                null,
                "price-chart-ohlc-compressed",
                "1",
                IntradayOpportunityPreparationWriter.ComputeSha256([1, 2, 3]));
            var prepared = CreatePreparation("current request") with
            {
                Attachments = [new IntradayOpportunityPreparedAttachment("ev_test", "Test chart", "image/png")],
                Evidence = [evidence],
            };
            var reviewer = new FakeReviewer("current request");
            var service = new IntradayOpportunityAnalysisService(reviewer, new WorkerOperationMetrics());

            var action = () => service.AnalyzeAsync(prepared);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*failed its SHA-256 integrity check*");
            reviewer.ReviewCalls.Should().Be(0);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task AnalyzeAsync_should_reject_evidence_assigned_to_a_different_market()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var evidencePath = Path.Combine(tempDirectory.FullName, "chart.png");
            var data = new byte[] { 1, 2, 3 };
            await File.WriteAllBytesAsync(evidencePath, data);
            var artifact = new ArtifactReference(evidencePath, new Uri(evidencePath).AbsoluteUri);
            var evidence = new DecisionEvidence(
                "ev_test",
                DecisionEvidenceKind.PriceChart,
                "EUR/USD chart",
                new InstrumentId("CS.D.EURUSD.CFD.IP"),
                "image/png",
                artifact,
                null,
                null,
                null,
                "price-chart-ohlc-compressed",
                "1",
                IntradayOpportunityPreparationWriter.ComputeSha256(data));
            var prepared = CreatePreparation("current request") with
            {
                Markets =
                [
                    new IntradayOpportunityPreparedMarket(
                        "CC.D.WTI.UMA.IP",
                        "WTI Crude Oil",
                        1,
                        80m,
                        80.2m,
                        80.1m,
                        0.2m,
                        DateTimeOffset.Parse("2026-07-03T00:55:00Z"),
                        PriceSeriesRefreshMode.LocalCache,
                        100,
                        ["ev_test"]),
                ],
                Evidence = [evidence],
            };
            var reviewer = new FakeReviewer("current request");
            var service = new IntradayOpportunityAnalysisService(reviewer, new WorkerOperationMetrics());

            var action = () => service.AnalyzeAsync(prepared);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*does not belong to prepared market*");
            reviewer.ReviewCalls.Should().Be(0);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    private static IntradayOpportunityPreparationDocument CreatePreparation(string renderedRequestText)
    {
        var requestedAtUtc = DateTimeOffset.Parse("2026-07-03T01:00:00Z");
        var artifact = new ArtifactReference(Path.GetFullPath("prepared.json"), new Uri(Path.GetFullPath("prepared.json")).AbsoluteUri);
        return new IntradayOpportunityPreparationDocument(
            new DateOnly(2026, 7, 3),
            requestedAtUtc,
            "intraday-opportunity-review",
            AutomationTestData.CreateIntradayReviewRequest(new DateOnly(2026, 7, 3), requestedAtUtc),
            renderedRequestText,
            [],
            [],
            artifact,
            artifact)
        {
            PromptContract = FakeReviewer.ContractValue,
            RequestSha256 = IntradayOpportunityPreparationWriter.ComputeSha256(
                System.Text.Encoding.UTF8.GetBytes(renderedRequestText)),
        };
    }

    private sealed class FakeReviewer(string renderedRequestText) : IIntradayOpportunityReviewer
    {
        public static PromptContractProvenance ContractValue { get; } = new(
            "intraday-opportunity-review",
            "1",
            new string('a', 64),
            "1",
            new string('b', 64));

        public PromptContractProvenance Contract => ContractValue;

        public int ReviewCalls { get; private set; }

        public string RenderRequestText(IntradayOpportunityReviewRequest request) => renderedRequestText;

        public Task<IntradayOpportunityReviewExecution> ReviewAsync(
            IntradayOpportunityReviewRequest request,
            IReadOnlyList<PromptAttachment> attachments,
            CancellationToken cancellationToken = default)
        {
            ReviewCalls++;
            throw new NotSupportedException();
        }
    }
}
