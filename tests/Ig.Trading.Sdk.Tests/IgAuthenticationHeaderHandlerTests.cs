using FluentAssertions;
using Ig.Trading.Sdk.Auth;
using Ig.Trading.Sdk.Configuration;
using Microsoft.Extensions.Options;
using System.Net;

namespace Ig.Trading.Sdk.Tests;

public class IgAuthenticationHeaderHandlerTests
{
    [Fact]
    public async Task SendAsync_ShouldAddApiKeyAndSessionTokens()
    {
        var options = Options.Create(new IgClientOptions
        {
            BaseUrl = "https://demo-api.ig.com/gateway/deal",
            ApiKey = "test-api-key",
            Identifier = "user",
            Password = "pass",
        });

        var store = new InMemoryIgSessionStore();
        store.Set(new IgSessionContext("cst-token", "sec-token", "ABC123", DateTimeOffset.UtcNow));

        var innerHandler = new RecordingHandler();
        var handler = new IgAuthenticationHeaderHandler(options, store)
        {
            InnerHandler = innerHandler,
        };

        var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://example.test/positions"), default);

        innerHandler.LastRequest.Should().NotBeNull();
        innerHandler.LastRequest!.Headers.GetValues("X-IG-API-KEY").Single().Should().Be("test-api-key");
        innerHandler.LastRequest.Headers.GetValues("CST").Single().Should().Be("cst-token");
        innerHandler.LastRequest.Headers.GetValues("X-SECURITY-TOKEN").Single().Should().Be("sec-token");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
