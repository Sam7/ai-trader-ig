using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.ClientModel;
using Trading.AI.Configuration;
using Trading.AI.DailyBriefing;
using Trading.AI.Observability;
using Trading.AI.Prompts;
using Trading.Strategy.Inputs;

public sealed class PromptExecutorTests
{
    [Fact]
    public async Task ExecuteStructuredAsync_ShouldRetryOnce_WhenResponseJsonIsInvalid()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var executor = CreateExecutor(tempDirectory.FullName);
            var context = CreateStructuredContext();
            var chatClient = new TestChatClient(
                _ => Task.FromResult(CreateResponse("{")),
                _ => Task.FromResult(CreateResponse("""
                    {
                      "macroSummary": "Macro",
                      "marketRegimeSummary": "Summary",
                      "marketRegime": "Mixed",
                      "rankedMarkets": [
                        {
                          "instrumentId": "CC.D.WTI.UMA.IP",
                          "instrumentName": "WTI Crude Oil",
                          "rank": 1,
                          "rationale": "Strongest",
                          "longScenario": {
                            "thesis": "Long",
                            "confirmation": "Confirm",
                            "invalidation": "Invalidate",
                            "expectedCatalysts": [],
                            "avoidTradingUntilUtc": null
                          },
                          "shortScenario": {
                            "thesis": "Short",
                            "confirmation": "Confirm",
                            "invalidation": "Invalidate",
                            "expectedCatalysts": [],
                            "avoidTradingUntilUtc": null
                          }
                        }
                      ],
                      "catalysts": [],
                      "opportunities": [],
                      "risks": [],
                      "calendarEvents": []
                    }
                    """)));

            var (_, structured) = await executor.ExecuteStructuredAsync<DailyPlanDocument>(chatClient, context, CancellationToken.None);

            chatClient.CallCount.Should().Be(2);
            structured.MarketRegime.Should().Be(MarketRegime.Mixed);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task ExecuteStructuredAsync_ShouldNotRetry_WhenTransportFails()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var executor = CreateExecutor(tempDirectory.FullName);
            var context = CreateStructuredContext();
            var chatClient = new TestChatClient(_ => throw new HttpRequestException("dns failed"));

            var action = () => executor.ExecuteStructuredAsync<DailyPlanDocument>(chatClient, context, CancellationToken.None);

            await action.Should().ThrowAsync<HttpRequestException>();
            chatClient.CallCount.Should().Be(1);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task ExecuteStructuredAsync_ShouldNotRetry_WhenProviderThrowsClientResultException()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var executor = CreateExecutor(tempDirectory.FullName);
            var context = CreateStructuredContext();
            var chatClient = new TestChatClient(_ => throw new ClientResultException("boom", null, null));

            var action = () => executor.ExecuteStructuredAsync<DailyPlanDocument>(chatClient, context, CancellationToken.None);

            await action.Should().ThrowAsync<ClientResultException>();
            chatClient.CallCount.Should().Be(1);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    private static PromptExecutor CreateExecutor(string observabilityRootPath)
    {
        var options = Options.Create(new DailyBriefingOptions
        {
            ObservabilityRootPath = observabilityRootPath,
        });

        return new PromptExecutor(
            new PromptRegistry(),
            new PromptTemplateRenderer(),
            new PromptObservabilityWriter(options));
    }

    private static PromptExecutionContext CreateStructuredContext()
        => new(
            PromptRegistry.DailyPlanJson,
            new DailyBriefingModelOptions { ModelId = "gpt-test" },
            new Dictionary<string, string>
            {
                ["TRADING_DATE"] = "2026-03-12",
                ["REPORT_TIMEZONE"] = "Australia/Melbourne",
                ["WATCHLIST_SIZE"] = "3",
                ["TRACKED_MARKETS"] = "- WTI Crude Oil | instrumentId: CC.D.WTI.UMA.IP | sector: Energy | aliases: WTI",
                ["RESEARCH_BRIEF"] = "# 1. Executive Snapshot",
            },
            DateTimeOffset.Parse("2026-03-12T06:30:45Z"),
            new DateOnly(2026, 3, 12),
            DailyPlanJsonResponseFormat.Create(3));

    private static ChatResponse CreateResponse(string text)
        => new(new ChatMessage(ChatRole.Assistant, text))
        {
            ModelId = "gpt-test",
            CreatedAt = DateTimeOffset.Parse("2026-03-12T06:31:00Z"),
        };

    private sealed class TestChatClient : IChatClient
    {
        private readonly Queue<Func<IReadOnlyList<ChatMessage>, Task<ChatResponse>>> _handlers;

        public TestChatClient(params Func<IReadOnlyList<ChatMessage>, Task<ChatResponse>>[] handlers)
        {
            _handlers = new Queue<Func<IReadOnlyList<ChatMessage>, Task<ChatResponse>>>(handlers);
        }

        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var handler = _handlers.Count > 1 ? _handlers.Dequeue() : _handlers.Peek();
            return handler(messages.ToArray());
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
