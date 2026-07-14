using FluentAssertions;
using Trading.Strategy.DayPlanning;

namespace Trading.Strategy.Tests;

public sealed class DailyPlanningPolicyTests
{
    [Theory]
    [InlineData(0, 2.0)]
    [InlineData(3, 0.0)]
    public void Validate_should_reject_invalid_values(int shortlistSize, double minimumRewardRiskRatio)
    {
        var policy = new DailyPlanningPolicy(shortlistSize, (decimal)minimumRewardRiskRatio);

        var action = policy.Validate;

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Default_should_describe_the_current_daily_plan_contract()
    {
        DailyPlanningPolicy.Default.ShortlistSize.Should().Be(3);
        DailyPlanningPolicy.Default.MinimumRewardRiskRatio.Should().Be(2.0m);
    }
}
