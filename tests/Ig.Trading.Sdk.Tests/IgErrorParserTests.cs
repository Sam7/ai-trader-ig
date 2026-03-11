using FluentAssertions;
using Ig.Trading.Sdk.Errors;
using System.Net;

namespace Ig.Trading.Sdk.Tests;

public sealed class IgErrorParserTests
{
    [Fact]
    public void Create_WithNonJsonContent_ShouldNotTreatBodyAsErrorCode()
    {
        var exception = IgErrorParser.Create(HttpStatusCode.BadRequest, "<html>market failed</html>");

        exception.ErrorCode.Should().BeNull();
        exception.Message.Should().Be("IG API request failed with status code 400.");
    }

    [Fact]
    public void Create_WithJsonButNoErrorCode_ShouldNotInventErrorCode()
    {
        var exception = IgErrorParser.Create(HttpStatusCode.BadRequest, """{"message":"market failed"}""");

        exception.ErrorCode.Should().BeNull();
        exception.Message.Should().Be("IG API request failed with status code 400.");
    }
}
