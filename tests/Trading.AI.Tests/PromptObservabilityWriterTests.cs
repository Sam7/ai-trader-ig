using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.DailyBriefing;
using Trading.AI.Observability;
using Trading.AI.PromptExecution;
using Trading.AI.Prompts;
using Trading.AI.Prompts.DailyPlanJson;
using Trading.Strategy.Inputs;

public sealed class PromptObservabilityWriterTests
{
    [Fact]
    public async Task StartAsync_AndFailAsync_ShouldWritePendingThenFailedJson()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var writer = new PromptObservabilityWriter(
                Options.Create(new PromptObservabilityOptions
                {
                    ObservabilityRootPath = tempDirectory.FullName,
                }));

            var invocation = new PromptInvocation(
                PromptRegistry.DailyBriefResearch,
                new PromptModelOptions { ModelId = "gpt-test" },
                new Dictionary<string, string> { ["REPORT_DATE"] = "2026-03-12" },
                new DateOnly(2026, 3, 12),
                DateTimeOffset.Parse("2026-03-12T06:30:45Z"),
                null,
                PromptTextArtifactKind.Markdown,
                []);

            var session = await writer.StartAsync(invocation, "request text", new { mode = "test" }, CancellationToken.None);

            File.Exists(session.JsonPath).Should().BeTrue();
            var pending = JsonDocument.Parse(await File.ReadAllTextAsync(session.JsonPath));
            pending.RootElement.GetProperty("status").GetString().Should().Be("Pending");

            await writer.WriteTextAsync(session, "# brief", CancellationToken.None);
            await writer.WriteStructuredAsync(session, new { marketRegime = "EventDriven" }, CancellationToken.None);
            await writer.FailAsync(session, invocation, "request text", null, new InvalidOperationException("boom"), TimeSpan.FromSeconds(2), CancellationToken.None);

            var failed = JsonDocument.Parse(await File.ReadAllTextAsync(session.JsonPath));
            failed.RootElement.GetProperty("status").GetString().Should().Be("Failed");
            failed.RootElement.GetProperty("error").GetString().Should().Contain("boom");
            failed.RootElement.GetProperty("textArtifactPath").GetString().Should().EndWith(".md");
            failed.RootElement.GetProperty("structuredArtifactPath").GetString().Should().EndWith("-extracted.json");
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task WriteStructuredAsync_ShouldSerializeEnumsAsStrings()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var writer = new PromptObservabilityWriter(
                Options.Create(new PromptObservabilityOptions
                {
                    ObservabilityRootPath = tempDirectory.FullName,
                }));

            var invocation = new PromptInvocation(
                PromptRegistry.DailyPlanJson,
                new PromptModelOptions { ModelId = "gpt-test" },
                new Dictionary<string, string> { ["TRADING_DATE"] = "2026-03-12" },
                new DateOnly(2026, 3, 12),
                DateTimeOffset.Parse("2026-03-12T06:30:45Z"),
                null,
                PromptTextArtifactKind.None,
                []);

            var session = await writer.StartAsync(invocation, "request text", null, CancellationToken.None);
            var document = new DailyPlanDocument(
                "Macro",
                "Summary",
                MarketRegime.EventDriven,
                [],
                [],
                [],
                [],
                []);

            await writer.WriteStructuredAsync(session, document, CancellationToken.None);

            var json = await File.ReadAllTextAsync(session.StructuredArtifactPath);
            json.Should().Contain("\"marketRegime\": \"EventDriven\"");
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task WriteAttachmentsAsync_ShouldPersistAttachmentPaths()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var writer = new PromptObservabilityWriter(
                Options.Create(new PromptObservabilityOptions
                {
                    ObservabilityRootPath = tempDirectory.FullName,
                }));

            var invocation = new PromptInvocation(
                PromptRegistry.IntradayOpportunityReview,
                new PromptModelOptions { ModelId = "gpt-test" },
                new Dictionary<string, string> { ["TRADING_DATE"] = "2026-03-12" },
                new DateOnly(2026, 3, 12),
                DateTimeOffset.Parse("2026-03-12T06:30:45Z"),
                null,
                PromptTextArtifactKind.None,
                []);

            var session = await writer.StartAsync(invocation, "request text", null, CancellationToken.None);
            await writer.WriteAttachmentsAsync(
                session,
                [new PromptAttachment("WTI Crude Oil chart", "image/png", [1, 2, 3])],
                CancellationToken.None);

            session.AttachmentArtifactPaths.Should().ContainSingle();
            File.Exists(session.AttachmentArtifactPaths[0]).Should().BeTrue();
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }
}
