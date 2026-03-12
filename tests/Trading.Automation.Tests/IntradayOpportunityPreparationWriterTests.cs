using FluentAssertions;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.Prompts.IntradayOpportunityReview;
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
            var writer = new IntradayOpportunityPreparationWriter(
                Options.Create(new PromptObservabilityOptions
                {
                    ObservabilityRootPath = tempDirectory.FullName,
                }));

            var prepared = await writer.WriteAsync(
                new DateOnly(2026, 3, 12),
                DateTimeOffset.Parse("2026-03-12T06:30:45Z"),
                new IntradayPreparedRun(
                    new IntradayOpportunityReviewInput(
                        new DateOnly(2026, 3, 12),
                        DateTimeOffset.Parse("2026-03-12T05:30:00Z"),
                        DateTimeOffset.Parse("2026-03-12T06:30:00Z"),
                        1,
                        4,
                        "Australia/Melbourne",
                        "Macro summary",
                        "Watched market context",
                        "No calendar events",
                        new DateOnly(2026, 3, 12),
                        DateTimeOffset.Parse("2026-03-12T06:30:45Z")),
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
                            "WTI chart",
                            [1, 2, 3, 4])
                    ]),
                CancellationToken.None);

            File.Exists(prepared.PreparedArtifact.Path).Should().BeTrue();
            File.Exists(prepared.RequestTextArtifact.Path).Should().BeTrue();
            prepared.Attachments.Should().ContainSingle();
            File.Exists(prepared.Attachments[0].Artifact.Path).Should().BeTrue();
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
}
