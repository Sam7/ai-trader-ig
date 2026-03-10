using FluentAssertions;
using Ig.Trading.Sdk.Configuration;

namespace Ig.Trading.Sdk.Tests;

public class IgClientOptionsTests
{
    [Fact]
    public void Validate_WithInvalidBaseUrl_ShouldThrow()
    {
        var options = new IgClientOptions
        {
            BaseUrl = "not-a-url",
            ApiKey = "key",
            Identifier = "id",
            Password = "pw",
        };

        var action = options.Validate;

        action.Should().Throw<InvalidOperationException>();
    }
}
