using FluentAssertions;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.Prompts;
using Trading.Abstractions;
using Trading.Automation.Execution;
using Trading.Strategy.Shared;

public sealed class IntradayOpportunityPreparationWriterTests
{
    [Fact]
    public async Task WriteAsync_ShouldPersistPreparationJsonRequestTextAndCharts()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var writer = CreateWriter(tempDirectory.FullName);
            var tradingDate = new DateOnly(2026, 3, 12);
            var requestedAtUtc = DateTimeOffset.Parse("2026-03-12T06:30:45Z");

            var prepared = await writer.WriteAsync(
                tradingDate,
                requestedAtUtc,
                CreatePreparedRun(tradingDate, requestedAtUtc),
                CancellationToken.None);

            File.Exists(prepared.PreparedArtifact.Path).Should().BeTrue();
            File.Exists(prepared.RequestTextArtifact.Path).Should().BeTrue();
            prepared.Attachments.Should().ContainSingle();
            prepared.Evidence.Should().ContainSingle();
            prepared.Markets[0].EvidenceIds.Should().Equal(prepared.Evidence[0].EvidenceId);
            prepared.Attachments[0].EvidenceId.Should().Be(prepared.Evidence[0].EvidenceId);
            File.Exists(prepared.Evidence[0].Artifact.Path).Should().BeTrue();
            prepared.Evidence[0].Sha256.Should().Be("9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a");
            prepared.RequestSha256.Should().MatchRegex("^[a-f0-9]{64}$");
            prepared.PreparedArtifact.Uri.Should().StartWith("file:///");

            var loaded = await writer.LoadAsync(prepared.PreparedArtifact.Path, CancellationToken.None);
            loaded.RenderedRequestText.Should().Be("Rendered request text");
            loaded.Markets.Should().ContainSingle();
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task WriteAsync_ShouldNotOverwriteAnExistingPreparation()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var writer = CreateWriter(tempDirectory.FullName);
            var tradingDate = new DateOnly(2026, 3, 12);
            var requestedAtUtc = DateTimeOffset.Parse("2026-03-12T06:30:45Z");
            var preparedRun = CreatePreparedRun(tradingDate, requestedAtUtc);
            var original = await writer.WriteAsync(tradingDate, requestedAtUtc, preparedRun);
            var originalRequestBytes = await File.ReadAllBytesAsync(original.RequestTextArtifact.Path);

            var action = () => writer.WriteAsync(tradingDate, requestedAtUtc, preparedRun);

            await action.Should().ThrowAsync<IOException>();
            (await File.ReadAllBytesAsync(original.RequestTextArtifact.Path)).Should().Equal(originalRequestBytes);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    private static IntradayOpportunityPreparationWriter CreateWriter(string rootPath)
        => new(Options.Create(new PromptObservabilityOptions
        {
            ObservabilityRootPath = rootPath,
        }));

    private static IntradayPreparedRun CreatePreparedRun(
        DateOnly tradingDate,
        DateTimeOffset requestedAtUtc)
        => new(
            AutomationTestData.CreateIntradayReviewRequest(tradingDate, requestedAtUtc),
            "Rendered request text",
            [
                new PreparedIntradayMarket(
                    new InstrumentId("CC.D.WTI.UMA.IP"),
                    "WTI Crude Oil",
                    1,
                    "Daily rationale",
                    new TradeScenario(TradeDirection.Buy, "Long thesis", "Confirm", "Invalidate", [], null),
                    new TradeScenario(TradeDirection.Sell, "Short thesis", "Confirm", "Invalidate", [], null),
                    80m,
                    80.2m,
                    80.1m,
                    0.2m,
                    DateTimeOffset.Parse("2026-03-12T06:20:00Z"),
                    PriceSeriesRefreshMode.Bootstrap,
                    576,
                    [new PreparedDecisionEvidence(
                        DecisionEvidenceKind.PriceChart,
                        "WTI chart",
                        "image/png",
                        [1, 2, 3, 4],
                        DateTimeOffset.Parse("2026-03-08T06:20:00Z"),
                        DateTimeOffset.Parse("2026-03-12T06:20:00Z"),
                        DateTimeOffset.Parse("2026-03-12T06:20:00Z"),
                        "price-chart-ohlc-compressed",
                        "1")])
            ],
            new PromptContractProvenance(
                "intraday-opportunity-review",
                "1",
                new string('a', 64),
                "1",
                new string('b', 64)),
            IntradayPreparationProfileReference.Default);
}
