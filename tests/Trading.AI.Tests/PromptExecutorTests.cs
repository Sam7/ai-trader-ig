using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.ClientModel;
using Trading.AI.Configuration;
using Trading.AI.DailyBriefing;
using Trading.AI.Observability;
using Trading.AI.PromptExecution;
using Trading.AI.Prompts;
using Trading.AI.Prompts.DailyBriefResearch;
using Trading.AI.Prompts.DailyPlanJson;
using Trading.Strategy.Inputs;

public sealed class PromptExecutorTests
{
    [Fact]
    public async Task ExecuteTextAsync_ShouldWriteMarkdownArtifactByDefault()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var chatClient = new TestChatClient(_ => Task.FromResult(CreateResponse("# brief")));
            var executor = CreateExecutor(tempDirectory.FullName, chatClient);

            var result = await executor.ExecuteTextAsync(
                PromptRegistry.DailyBriefResearch,
                new PromptModelOptions { ModelId = "gpt-test" },
                new DailyBriefResearchInput(
                    new DateOnly(2026, 3, 12),
                    "Australia/Melbourne",
                    3,
                    "- WTI Crude Oil | instrumentId: CC.D.WTI.UMA.IP | sector: Energy | aliases: WTI",
                    new DateOnly(2026, 3, 12),
                    DateTimeOffset.Parse("2026-03-12T06:30:45Z")),
                cancellationToken: CancellationToken.None);

            result.TextArtifactPath.Should().EndWith(".md");
            File.Exists(result.TextArtifactPath).Should().BeTrue();
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task ExecuteStructuredAsync_ShouldRetryOnce_WhenResponseJsonIsInvalid()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
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
            var executor = CreateExecutor(tempDirectory.FullName, chatClient);

            var structured = await executor.ExecuteStructuredAsync<DailyPlanJsonInput, DailyPlanDocument>(
                PromptRegistry.DailyPlanJson,
                new PromptModelOptions { ModelId = "gpt-test" },
                CreateStructuredInput(),
                DailyPlanJsonResponseFormat.Create(3),
                CancellationToken.None);

            chatClient.CallCount.Should().Be(2);
            structured.StructuredValue.MarketRegime.Should().Be(MarketRegime.Mixed);
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
            var chatClient = new TestChatClient(_ => throw new HttpRequestException("dns failed"));
            var executor = CreateExecutor(tempDirectory.FullName, chatClient);

            var action = () => executor.ExecuteStructuredAsync<DailyPlanJsonInput, DailyPlanDocument>(
                PromptRegistry.DailyPlanJson,
                new PromptModelOptions { ModelId = "gpt-test" },
                CreateStructuredInput(),
                DailyPlanJsonResponseFormat.Create(3),
                CancellationToken.None);

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
            var chatClient = new TestChatClient(_ => throw new ClientResultException("boom", null, null));
            var executor = CreateExecutor(tempDirectory.FullName, chatClient);

            var action = () => executor.ExecuteStructuredAsync<DailyPlanJsonInput, DailyPlanDocument>(
                PromptRegistry.DailyPlanJson,
                new PromptModelOptions { ModelId = "gpt-test" },
                CreateStructuredInput(),
                DailyPlanJsonResponseFormat.Create(3),
                CancellationToken.None);

            await action.Should().ThrowAsync<ClientResultException>();
            chatClient.CallCount.Should().Be(1);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    private static PromptExecutor CreateExecutor(string observabilityRootPath, IChatClient chatClient)
    {
        var options = Options.Create(new PromptObservabilityOptions
        {
            ObservabilityRootPath = observabilityRootPath,
        });

        return new PromptExecutor(
            new PromptRegistry(),
            new PromptTemplateRenderer(),
            new PromptObservabilityWriter(options),
            new StubChatClientFactory(chatClient),
            new PromptInputConverter());
    }

    private static DailyPlanJsonInput CreateStructuredInput()
        => new(
            new DateOnly(2026, 3, 12),
            "Australia/Melbourne",
            3,
            2m,
            "- WTI Crude Oil | instrumentId: CC.D.WTI.UMA.IP | sector: Energy | aliases: WTI",
            "# 1. Executive Snapshot",
            DateTimeOffset.Parse("2026-03-12T06:30:45Z"));

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

    private sealed class StubChatClientFactory : IChatClientFactory
    {
        private readonly IChatClient _chatClient;

        public StubChatClientFactory(IChatClient chatClient)
        {
            _chatClient = chatClient;
        }

        public IChatClient CreateClient(string modelId)
            => _chatClient;
    }
}
