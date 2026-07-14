using FluentAssertions;
using Trading.Worker.Diagnostics;

public sealed class SyntheticWorkerLoadOptionsTests
{
    [Fact]
    public void Validate_should_reject_a_non_positive_allocation_interval()
    {
        var options = new SyntheticWorkerLoadOptions
        {
            AllocationInterval = TimeSpan.Zero,
        };

        var action = options.Validate;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*allocation interval*");
    }
}
