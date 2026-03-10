using FluentAssertions;
using Trading.Abstractions;

namespace Trading.Abstractions.Tests;

public class OrderQueryTests
{
    [Fact]
    public void Validate_WithToBeforeFrom_ShouldThrow()
    {
        var query = new OrderQuery(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(-1), 100);

        var action = query.Validate;

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_WithInvalidMaxItems_ShouldThrow()
    {
        var query = new OrderQuery(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, 0);

        var action = query.Validate;

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
