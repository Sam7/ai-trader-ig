using FluentAssertions;
using Trading.AI.Prompts;

public sealed class PromptContractTests
{
    [Fact]
    public void DailyBriefResearch_ShouldRequireTrackedMarketWatchSection()
    {
        var registry = new PromptRegistry();

        var prompt = registry.GetPromptText(PromptRegistry.DailyBriefResearch);

        prompt.Should().Contain("TRACKED_MARKETS");
        prompt.Should().Contain("WATCHLIST_SIZE");
        prompt.Should().Contain("## 11.5 Assets To Watch Today");
        prompt.Should().Contain("Canonical market regime");
        prompt.Should().Contain("deep market contemplation");
        prompt.Should().Contain("causal reasoning");
        prompt.Should().Contain("keep the written brief citation-free");
        prompt.Should().NotContain("High-Signal Source Notes");
    }

    [Fact]
    public void DailyPlanJson_ShouldBeAnExtractionPrompt()
    {
        var registry = new PromptRegistry();

        var prompt = registry.GetPromptText(PromptRegistry.DailyPlanJson);

        prompt.Should().ContainEquivalentOf("prefer the section titled `## 12.5 Assets To Watch Today`");
        prompt.Should().ContainEquivalentOf("watchList` must contain the same");
        prompt.Should().ContainEquivalentOf("calendarEvents` must be an empty array");
        prompt.Should().ContainEquivalentOf("marketRegime` must be exactly one of");
        prompt.Should().NotContain("entryZoneLowerBound");
    }
}
