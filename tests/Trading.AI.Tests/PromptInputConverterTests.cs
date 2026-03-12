using FluentAssertions;
using Trading.AI.PromptExecution;
using Trading.AI.Prompts.DailyBriefResearch;

public sealed class PromptInputConverterTests
{
    [Fact]
    public void Convert_ShouldMapAnonymousObjectToPromptVariablesAndMetadata()
    {
        var converter = new PromptInputConverter();
        var requestedAtUtc = DateTimeOffset.Parse("2026-03-12T06:30:45Z");

        var converted = converter.Convert(new DailyBriefResearchInput(
            new DateOnly(2026, 3, 12),
            "Australia/Melbourne",
            3,
            "- WTI",
            new DateOnly(2026, 3, 12),
            requestedAtUtc));

        converted.PromptDate.Should().Be(new DateOnly(2026, 3, 12));
        converted.RequestedAtUtc.Should().Be(requestedAtUtc);
        converted.Variables["PROMPT_DATE"].Should().Be("2026-03-12");
        converted.Variables["WATCHLIST_SIZE"].Should().Be("3");
        converted.Variables["REPORT_TIMEZONE"].Should().Be("Australia/Melbourne");
    }

    [Fact]
    public void Convert_ShouldRejectComplexPropertyTypes()
    {
        var converter = new PromptInputConverter();

        var action = () => converter.Convert(new
        {
            TrackedMarkets = new[] { "WTI" },
        });

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*TrackedMarkets*unsupported type*");
    }
}
