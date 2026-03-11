using FluentAssertions;
using Trading.AI.Prompts;

public sealed class PromptRegistryTests
{
    [Fact]
    public void GetPromptText_ForResearchPrompt_ShouldLoadEmbeddedMarkdown()
    {
        var registry = new PromptRegistry();

        var prompt = registry.GetPromptText(PromptRegistry.DailyBriefResearch);

        prompt.Should().Contain("REPORT_DATE");
        prompt.Should().Contain("# 1. Executive Snapshot");
    }

    [Fact]
    public void GetById_ForKnownPrompt_ShouldReturnRegisteredDefinition()
    {
        var registry = new PromptRegistry();

        var definition = registry.GetById("daily-plan-json");

        definition.Should().Be(PromptRegistry.DailyPlanJson);
    }
}
