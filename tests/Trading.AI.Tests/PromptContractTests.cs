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
        prompt.Should().NotContain("Source note:");
    }

    [Fact]
    public void DailyPlanJson_ShouldBeAnExtractionPrompt()
    {
        var registry = new PromptRegistry();

        var prompt = registry.GetPromptText(PromptRegistry.DailyPlanJson);

        prompt.Should().ContainEquivalentOf("prefer the section titled `## 11.5 Assets To Watch Today`");
        prompt.Should().ContainEquivalentOf("instrumentName");
        prompt.Should().ContainEquivalentOf("catalysts");
        prompt.Should().ContainEquivalentOf("opportunities");
        prompt.Should().ContainEquivalentOf("risks");
        prompt.Should().ContainEquivalentOf("calendarEvents` must include every `EVT-##` row");
        prompt.Should().ContainEquivalentOf("impact` must be exactly one of: `Low`, `Medium`, `High`");
        prompt.Should().ContainEquivalentOf("marketRegime` must be exactly one of");
        prompt.Should().NotContain("entryZoneLowerBound");
    }

    [Fact]
    public void IntradayOpportunityReview_ShouldRequireDeepReasoningAndStructuredTradingFields()
    {
        var registry = new PromptRegistry();

        var prompt = registry.GetPromptText(PromptRegistry.IntradayOpportunityReview);

        prompt.Should().Contain("Your job is not to aggregate headlines");
        prompt.Should().Contain("deep judgement");
        prompt.Should().Contain("rewardRiskRatio");
        prompt.Should().Contain("currentSpread");
        prompt.Should().Contain("candidateOpportunities");
        prompt.Should().Contain("stand aside");
    }
}
