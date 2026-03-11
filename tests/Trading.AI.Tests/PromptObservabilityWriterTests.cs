using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.DailyBriefing;
using Trading.AI.Observability;
using Trading.AI.Prompts;

public sealed class PromptObservabilityWriterTests
{
    [Fact]
    public async Task StartAsync_AndFailAsync_ShouldWritePendingThenFailedJson()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var writer = new PromptObservabilityWriter(
                Options.Create(new DailyBriefingOptions
                {
                    ObservabilityRootPath = tempDirectory.FullName,
                }));

            var context = new PromptExecutionContext(
                PromptRegistry.DailyBriefResearch,
                new DailyBriefingModelOptions { ModelId = "gpt-test" },
                new Dictionary<string, string> { ["REPORT_DATE"] = "2026-03-12" },
                DateTimeOffset.Parse("2026-03-12T06:30:45Z"),
                new DateOnly(2026, 3, 12));

            var session = await writer.StartAsync(context, "request text", new { mode = "test" }, CancellationToken.None);

            File.Exists(session.JsonPath).Should().BeTrue();
            var pending = JsonDocument.Parse(await File.ReadAllTextAsync(session.JsonPath));
            pending.RootElement.GetProperty("status").GetString().Should().Be("Pending");

            await writer.WriteMarkdownAsync(session, "# brief", CancellationToken.None);
            await writer.FailAsync(session, context, "request text", null, new InvalidOperationException("boom"), TimeSpan.FromSeconds(2), CancellationToken.None);

            var failed = JsonDocument.Parse(await File.ReadAllTextAsync(session.JsonPath));
            failed.RootElement.GetProperty("status").GetString().Should().Be("Failed");
            failed.RootElement.GetProperty("error").GetString().Should().Contain("boom");
            failed.RootElement.GetProperty("markdownArtifactPath").GetString().Should().EndWith(".md");
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }
}
