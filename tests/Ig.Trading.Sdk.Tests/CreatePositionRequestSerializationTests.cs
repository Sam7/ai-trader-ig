using System.Text.Json;
using FluentAssertions;
using Ig.Trading.Sdk.Models;

namespace Ig.Trading.Sdk.Tests;

public class CreatePositionRequestSerializationTests
{
    [Fact]
    public void Serialize_WithNoProtection_ShouldOmitOptionalProtectionFields()
    {
        var request = CreateRequest();

        var json = JsonSerializer.Serialize(request, JsonOptions);

        json.Should().NotContain("stopLevel");
        json.Should().NotContain("stopDistance");
        json.Should().NotContain("limitLevel");
        json.Should().NotContain("limitDistance");
        json.Should().NotContain("trailingStop");
        json.Should().NotContain("trailingStopIncrement");
    }

    [Fact]
    public void Serialize_WithProtection_ShouldIncludeProvidedProtectionFields()
    {
        var request = CreateRequest() with
        {
            StopLevel = 95m,
            LimitLevel = 110m,
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("stopLevel").GetDecimal().Should().Be(95m);
        root.GetProperty("limitLevel").GetDecimal().Should().Be(110m);
        root.TryGetProperty("stopDistance", out _).Should().BeFalse();
        root.TryGetProperty("limitDistance", out _).Should().BeFalse();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static CreatePositionRequest CreateRequest()
        => new(
            "CS.D.BITCOIN.CFM.IP",
            "-",
            "BUY",
            0.01m,
            "MARKET",
            "AUD",
            "FILL_OR_KILL",
            ForceOpen: true,
            GuaranteedStop: false,
            "BASELINE123");
}
