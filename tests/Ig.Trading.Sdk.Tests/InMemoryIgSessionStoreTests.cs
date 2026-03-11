using FluentAssertions;
using Ig.Trading.Sdk.Auth;

namespace Ig.Trading.Sdk.Tests;

public class InMemoryIgSessionStoreTests
{
    [Fact]
    public void Set_ShouldUpdateCurrentContext()
    {
        var store = new InMemoryIgSessionStore();
        var expected = new IgSessionContext("cst", "token", "ACC1", DateTimeOffset.UtcNow);

        store.Set(expected);

        store.Current.Should().Be(expected);
    }

    [Fact]
    public void Clear_ShouldResetCurrentContext()
    {
        var store = new InMemoryIgSessionStore();
        store.Set(new IgSessionContext("cst", "token", "ACC1", DateTimeOffset.UtcNow));

        store.Clear();

        store.Current.Cst.Should().BeNull();
        store.Current.SecurityToken.Should().BeNull();
        store.Current.CurrentAccountId.Should().BeNull();
        store.Current.TimezoneOffsetHours.Should().BeNull();
    }
}
