using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using Trading.AI.Configuration;
using Trading.AI.DailyBriefing;
using Trading.AI.Observability;
using Trading.AI.PromptExecution;
using Trading.AI.Prompts;
using Trading.AI.Prompts.DailyBriefResearch;
using Trading.AI.Prompts.DailyPlanJson;
using Trading.AI.Prompts.IntradayOpportunityReview;
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
            result.EnvelopeArtifactPath.Should().EndWith(".json");
            File.Exists(result.EnvelopeArtifactPath).Should().BeTrue();
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
    public async Task ExecuteStructuredAsync_ShouldRetryTransientTransportFailureUntilMaximumAttempts()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var chatClient = new TestChatClient(_ => throw new HttpRequestException("dns failed"));
            var executor = CreateExecutor(tempDirectory.FullName, chatClient, (_, _) => Task.CompletedTask);

            var action = () => executor.ExecuteStructuredAsync<DailyPlanJsonInput, DailyPlanDocument>(
                PromptRegistry.DailyPlanJson,
                new PromptModelOptions { ModelId = "gpt-test" },
                CreateStructuredInput(),
                DailyPlanJsonResponseFormat.Create(3),
                CancellationToken.None);

            await action.Should().ThrowAsync<HttpRequestException>();
            chatClient.CallCount.Should().Be(3);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task ExecuteStructuredAsync_ShouldRetryTransientClientResultException()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var chatClient = new TestChatClient(
                _ => throw CreateClientResultException(520),
                _ => Task.FromResult(CreateValidDailyPlanResponse()));
            var executor = CreateExecutor(tempDirectory.FullName, chatClient, (_, _) => Task.CompletedTask);

            var result = await executor.ExecuteStructuredAsync<DailyPlanJsonInput, DailyPlanDocument>(
                PromptRegistry.DailyPlanJson,
                new PromptModelOptions { ModelId = "gpt-test" },
                CreateStructuredInput(),
                DailyPlanJsonResponseFormat.Create(3),
                CancellationToken.None);

            chatClient.CallCount.Should().Be(2);
            result.StructuredValue.MarketRegime.Should().Be(MarketRegime.Mixed);

            var record = ReadLatestObservation(tempDirectory.FullName);
            record.RootElement.GetProperty("attempts").GetArrayLength().Should().Be(2);
            record.RootElement.GetProperty("attempts")[0].GetProperty("httpStatus").GetInt32().Should().Be(520);
            record.RootElement.GetProperty("attempts")[1].GetProperty("status").GetString().Should().Be("Completed");
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task ExecuteStructuredAsync_ShouldFailAfterMaximumTransientAttempts()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var chatClient = new TestChatClient(_ => throw CreateClientResultException(520));
            var executor = CreateExecutor(tempDirectory.FullName, chatClient, (_, _) => Task.CompletedTask);

            var action = () => executor.ExecuteStructuredAsync<DailyPlanJsonInput, DailyPlanDocument>(
                PromptRegistry.DailyPlanJson,
                new PromptModelOptions { ModelId = "gpt-test" },
                CreateStructuredInput(),
                DailyPlanJsonResponseFormat.Create(3),
                CancellationToken.None);

            await action.Should().ThrowAsync<ClientResultException>();
            chatClient.CallCount.Should().Be(3);

            var record = ReadLatestObservation(tempDirectory.FullName);
            record.RootElement.GetProperty("attempts").GetArrayLength().Should().Be(3);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task ExecuteStructuredAsync_ShouldNotRetryPermanentClientResultException()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var chatClient = new TestChatClient(_ => throw CreateClientResultException(400));
            var executor = CreateExecutor(tempDirectory.FullName, chatClient, (_, _) => Task.CompletedTask);

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

    [Fact]
    public async Task ExecuteStructuredAsync_ShouldStopRetryingWhenCancellationIsRequested()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            using var cancellationSource = new CancellationTokenSource();
            var chatClient = new TestChatClient(
                _ => throw CreateClientResultException(520),
                _ => Task.FromResult(CreateValidDailyPlanResponse()));
            var executor = CreateExecutor(
                tempDirectory.FullName,
                chatClient,
                (_, token) =>
                {
                    cancellationSource.Cancel();
                    return Task.Delay(TimeSpan.FromMinutes(1), token);
                });

            var action = () => executor.ExecuteStructuredAsync<DailyPlanJsonInput, DailyPlanDocument>(
                PromptRegistry.DailyPlanJson,
                new PromptModelOptions { ModelId = "gpt-test" },
                CreateStructuredInput(),
                DailyPlanJsonResponseFormat.Create(3),
                cancellationSource.Token);

            await action.Should().ThrowAsync<OperationCanceledException>();
            chatClient.CallCount.Should().Be(1);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task ExecuteStructuredAsync_WithAttachments_ShouldPersistAttachmentArtifacts()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var chatClient = new TestChatClient(messages =>
            {
                messages.Should().ContainSingle();
                messages.Single().Contents.Should().HaveCountGreaterThan(1);

                return Task.FromResult(CreateResponse("""
                    {
                      "recentDevelopmentsSummary": "USD softer and energy stable.",
                      "marketAssessments": [
                        {
                          "instrumentId": "CC.D.WTI.UMA.IP",
                          "instrumentName": "WTI Crude Oil",
                          "opportunityScore": 68,
                          "directionalBias": "Buy",
                          "summary": "Constructive intraday structure.",
                          "whyNow": "Momentum improved in the last hour.",
                          "standAsideReason": ""
                        }
                      ],
                      "candidateOpportunities": []
                    }
                    """));
            });
            var executor = CreateExecutor(tempDirectory.FullName, chatClient);

            var result = await executor.ExecuteStructuredAsync<IntradayOpportunityReviewInput, IntradayOpportunityReviewDocument>(
                PromptRegistry.IntradayOpportunityReview,
                new PromptModelOptions { ModelId = "gpt-test", EnableWebSearch = true },
                new IntradayOpportunityReviewInput(
                    new DateOnly(2026, 3, 12),
                    DateTimeOffset.Parse("2026-03-12T05:30:00Z"),
                    DateTimeOffset.Parse("2026-03-12T06:30:00Z"),
                    1,
                    4,
                    "Australia/Melbourne",
                    "Macro summary",
                    "One watched market",
                    "No calendar events",
                    new DateOnly(2026, 3, 12),
                    DateTimeOffset.Parse("2026-03-12T06:30:45Z")),
                [new PromptAttachment("WTI Crude Oil chart", "image/png", [1, 2, 3, 4])],
                IntradayOpportunityReviewResponseFormat.Create(),
                CancellationToken.None);

            result.StructuredValue.MarketAssessments.Should().HaveCount(1);
            result.AttachmentArtifactPaths.Should().ContainSingle();
            File.Exists(result.AttachmentArtifactPaths[0]).Should().BeTrue();
            File.Exists(result.EnvelopeArtifactPath).Should().BeTrue();
            File.Exists(result.StructuredArtifactPath).Should().BeTrue();
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task ExecuteStructuredAsync_WithBackgroundResponses_ShouldPollAndPersistProviderMetadata()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var chatClient = new TestChatClient(_ => throw new InvalidOperationException("Synchronous client should not be called."));
            var backgroundClient = new TestBackgroundResponseClient(
                createHandlers:
                [
                    (_, messages, _) =>
                    {
                        messages.Should().ContainSingle();
                        messages.Single().Contents.Should().HaveCountGreaterThan(1);
                        return Task.FromResult(new BackgroundResponseResult("resp_test", "queued", null));
                    }
                ],
                getHandlers:
                [
                    (_, _, _) => Task.FromResult(new BackgroundResponseResult("resp_test", "in_progress", null)),
                    (_, _, _) => Task.FromResult(new BackgroundResponseResult("resp_test", "completed", CreateValidIntradayResponse()))
                ]);
            var executor = CreateExecutor(
                tempDirectory.FullName,
                chatClient,
                (_, _) => Task.CompletedTask,
                backgroundClient);

            var result = await executor.ExecuteStructuredAsync<IntradayOpportunityReviewInput, IntradayOpportunityReviewDocument>(
                PromptRegistry.IntradayOpportunityReview,
                new PromptModelOptions
                {
                    ModelId = "gpt-test",
                    EnableWebSearch = true,
                    UseBackgroundResponses = true,
                    BackgroundPollInterval = TimeSpan.FromMilliseconds(1),
                },
                CreateIntradayInput(),
                [new PromptAttachment("WTI Crude Oil chart", "image/png", [1, 2, 3, 4])],
                IntradayOpportunityReviewResponseFormat.Create(),
                CancellationToken.None);

            chatClient.CallCount.Should().Be(0);
            backgroundClient.CreateCount.Should().Be(1);
            backgroundClient.GetCount.Should().Be(2);
            backgroundClient.CancelCount.Should().Be(0);
            result.StructuredValue.MarketAssessments.Should().ContainSingle();

            var record = ReadLatestObservation(tempDirectory.FullName);
            record.RootElement.GetProperty("status").GetString().Should().Be("Completed");
            record.RootElement.GetProperty("processingMode").GetString().Should().Be("ResponsesBackground");
            record.RootElement.GetProperty("providerResponseId").GetString().Should().Be("resp_test");
            record.RootElement.GetProperty("providerStatus").GetString().Should().Be("completed");
            record.RootElement.GetProperty("attempts")
                .EnumerateArray()
                .Select(attempt => attempt.GetProperty("phase").GetString())
                .Should()
                .Contain(["CreateBackgroundResponse", "PollBackgroundResponse"]);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task ExecuteStructuredAsync_WithBackgroundResponses_ShouldRetryPoll520WithoutCreatingAgain()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var backgroundClient = new TestBackgroundResponseClient(
                createHandlers:
                [
                    (_, _, _) => Task.FromResult(new BackgroundResponseResult("resp_test", "queued", null))
                ],
                getHandlers:
                [
                    (_, _, _) => throw CreateClientResultException(520),
                    (_, _, _) => Task.FromResult(new BackgroundResponseResult("resp_test", "completed", CreateValidIntradayResponse()))
                ]);
            var executor = CreateExecutor(
                tempDirectory.FullName,
                new TestChatClient(_ => throw new InvalidOperationException("Synchronous client should not be called.")),
                (_, _) => Task.CompletedTask,
                backgroundClient);

            var result = await executor.ExecuteStructuredAsync<IntradayOpportunityReviewInput, IntradayOpportunityReviewDocument>(
                PromptRegistry.IntradayOpportunityReview,
                new PromptModelOptions
                {
                    ModelId = "gpt-test",
                    UseBackgroundResponses = true,
                    BackgroundPollInterval = TimeSpan.FromMilliseconds(1),
                },
                CreateIntradayInput(),
                [],
                IntradayOpportunityReviewResponseFormat.Create(),
                CancellationToken.None);

            result.StructuredValue.MarketAssessments.Should().ContainSingle();
            backgroundClient.CreateCount.Should().Be(1);
            backgroundClient.GetCount.Should().Be(2);

            var record = ReadLatestObservation(tempDirectory.FullName);
            var attempts = record.RootElement.GetProperty("attempts").EnumerateArray().ToArray();
            attempts.Should().Contain(attempt =>
                attempt.GetProperty("phase").GetString() == "PollBackgroundResponse"
                && attempt.GetProperty("status").GetString() == "Failed"
                && attempt.GetProperty("httpStatus").GetInt32() == 520);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task ExecuteStructuredAsync_WithBackgroundResponses_ShouldRetryCreate520UntilMaximumAttempts()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var backgroundClient = new TestBackgroundResponseClient(
                createHandlers:
                [
                    (_, _, _) => throw CreateClientResultException(520)
                ],
                getHandlers: []);
            var executor = CreateExecutor(
                tempDirectory.FullName,
                new TestChatClient(_ => throw new InvalidOperationException("Synchronous client should not be called.")),
                (_, _) => Task.CompletedTask,
                backgroundClient);

            var action = () => executor.ExecuteStructuredAsync<IntradayOpportunityReviewInput, IntradayOpportunityReviewDocument>(
                PromptRegistry.IntradayOpportunityReview,
                new PromptModelOptions
                {
                    ModelId = "gpt-test",
                    UseBackgroundResponses = true,
                    BackgroundPollInterval = TimeSpan.FromMilliseconds(1),
                },
                CreateIntradayInput(),
                [],
                IntradayOpportunityReviewResponseFormat.Create(),
                CancellationToken.None);

            await action.Should().ThrowAsync<ClientResultException>();
            backgroundClient.CreateCount.Should().Be(3);
            backgroundClient.GetCount.Should().Be(0);

            var record = ReadLatestObservation(tempDirectory.FullName);
            record.RootElement.GetProperty("status").GetString().Should().Be("Failed");
            record.RootElement.GetProperty("providerResponseId").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task ExecuteStructuredAsync_WithBackgroundResponses_ShouldCancelOnPollTimeout()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var backgroundClient = new TestBackgroundResponseClient(
                createHandlers:
                [
                    (_, _, _) => Task.FromResult(new BackgroundResponseResult("resp_test", "queued", null))
                ],
                getHandlers: []);
            var executor = CreateExecutor(
                tempDirectory.FullName,
                new TestChatClient(_ => throw new InvalidOperationException("Synchronous client should not be called.")),
                (_, _) => Task.CompletedTask,
                backgroundClient);

            var action = () => executor.ExecuteStructuredAsync<IntradayOpportunityReviewInput, IntradayOpportunityReviewDocument>(
                PromptRegistry.IntradayOpportunityReview,
                new PromptModelOptions
                {
                    ModelId = "gpt-test",
                    UseBackgroundResponses = true,
                    BackgroundPollInterval = TimeSpan.FromMilliseconds(1),
                    BackgroundPollTimeout = TimeSpan.Zero,
                },
                CreateIntradayInput(),
                [],
                IntradayOpportunityReviewResponseFormat.Create(),
                CancellationToken.None);

            await action.Should().ThrowAsync<TimeoutException>();
            backgroundClient.CreateCount.Should().Be(1);
            backgroundClient.GetCount.Should().Be(0);
            backgroundClient.CancelCount.Should().Be(1);

            var record = ReadLatestObservation(tempDirectory.FullName);
            record.RootElement.GetProperty("status").GetString().Should().Be("Failed");
            record.RootElement.GetProperty("providerResponseId").GetString().Should().Be("resp_test");
            record.RootElement.GetProperty("providerStatus").GetString().Should().Be("cancelled");
            record.RootElement.GetProperty("attempts")
                .EnumerateArray()
                .Select(attempt => attempt.GetProperty("phase").GetString())
                .Should()
                .Contain("CancelBackgroundResponse");
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public void RenderRequestText_ShouldMatchPromptTemplateRendering()
    {
        var executor = CreateExecutor(Path.GetTempPath(), new TestChatClient(_ => Task.FromResult(CreateResponse("unused"))));

        var text = executor.RenderRequestText(
            PromptRegistry.IntradayOpportunityReview,
            new IntradayOpportunityReviewInput(
                new DateOnly(2026, 3, 12),
                DateTimeOffset.Parse("2026-03-12T05:30:00Z"),
                DateTimeOffset.Parse("2026-03-12T06:30:00Z"),
                1,
                4,
                "Australia/Melbourne",
                "Macro summary",
                "One watched market",
                "No calendar events",
                new DateOnly(2026, 3, 12),
                DateTimeOffset.Parse("2026-03-12T06:30:45Z")));

        text.Should().Contain("Trading date: 2026-03-12");
        text.Should().Contain("Maximum actionable candidates: 4");
        text.Should().Contain("One watched market");
    }

    private static PromptExecutor CreateExecutor(
        string observabilityRootPath,
        IChatClient chatClient,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        IBackgroundResponseClient? backgroundResponseClient = null)
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
            new PromptInputConverter(),
            backgroundResponseClient,
            delayAsync);
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

    private static ChatResponse CreateValidDailyPlanResponse()
        => CreateResponse("""
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
            """);

    private static IntradayOpportunityReviewInput CreateIntradayInput()
        => new(
            new DateOnly(2026, 3, 12),
            DateTimeOffset.Parse("2026-03-12T05:30:00Z"),
            DateTimeOffset.Parse("2026-03-12T06:30:00Z"),
            1,
            4,
            "Australia/Melbourne",
            "Macro summary",
            "One watched market",
            "No calendar events",
            new DateOnly(2026, 3, 12),
            DateTimeOffset.Parse("2026-03-12T06:30:45Z"));

    private static ChatResponse CreateValidIntradayResponse()
        => CreateResponse("""
            {
              "recentDevelopmentsSummary": "USD softer and energy stable.",
              "marketAssessments": [
                {
                  "instrumentId": "CC.D.WTI.UMA.IP",
                  "instrumentName": "WTI Crude Oil",
                  "opportunityScore": 68,
                  "directionalBias": "Buy",
                  "summary": "Constructive intraday structure.",
                  "whyNow": "Momentum improved in the last hour.",
                  "standAsideReason": ""
                }
              ],
              "candidateOpportunities": []
            }
            """);

    private static ClientResultException CreateClientResultException(int status)
        => new("Service request failed.", new TestPipelineResponse(status), null);

    private static JsonDocument ReadLatestObservation(string observabilityRootPath)
    {
        var jsonPath = Directory.GetFiles(observabilityRootPath, "*.json", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("-extracted.json", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .First();

        return JsonDocument.Parse(File.ReadAllText(jsonPath));
    }

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

    private sealed class TestBackgroundResponseClient : IBackgroundResponseClient
    {
        private readonly Queue<Func<PromptInvocation, IReadOnlyList<ChatMessage>, ChatOptions, Task<BackgroundResponseResult>>> _createHandlers;
        private readonly Queue<Func<string, string, ChatOptions, Task<BackgroundResponseResult>>> _getHandlers;

        public TestBackgroundResponseClient(
            IReadOnlyList<Func<PromptInvocation, IReadOnlyList<ChatMessage>, ChatOptions, Task<BackgroundResponseResult>>> createHandlers,
            IReadOnlyList<Func<string, string, ChatOptions, Task<BackgroundResponseResult>>> getHandlers)
        {
            _createHandlers = new Queue<Func<PromptInvocation, IReadOnlyList<ChatMessage>, ChatOptions, Task<BackgroundResponseResult>>>(createHandlers);
            _getHandlers = new Queue<Func<string, string, ChatOptions, Task<BackgroundResponseResult>>>(getHandlers);
        }

        public int CreateCount { get; private set; }

        public int GetCount { get; private set; }

        public int CancelCount { get; private set; }

        public Task<BackgroundResponseResult> CreateAsync(
            PromptInvocation invocation,
            IReadOnlyList<ChatMessage> messages,
            ChatOptions options,
            CancellationToken cancellationToken)
        {
            CreateCount++;
            var handler = _createHandlers.Count > 1 ? _createHandlers.Dequeue() : _createHandlers.Peek();
            return handler(invocation, messages, options);
        }

        public Task<BackgroundResponseResult> GetAsync(
            string modelId,
            string responseId,
            ChatOptions options,
            CancellationToken cancellationToken)
        {
            GetCount++;
            var handler = _getHandlers.Count > 1 ? _getHandlers.Dequeue() : _getHandlers.Peek();
            return handler(modelId, responseId, options);
        }

        public Task CancelAsync(string modelId, string responseId, CancellationToken cancellationToken)
        {
            CancelCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestPipelineResponse : PipelineResponse
    {
        public TestPipelineResponse(int status)
        {
            Status = status;
        }

        public override int Status { get; }

        public override string ReasonPhrase => "test";

        protected override PipelineResponseHeaders HeadersCore { get; } = new TestPipelineResponseHeaders();

        public override Stream? ContentStream { get; set; } = Stream.Null;

        public override BinaryData Content => BinaryData.FromString("{}");

        public override bool IsError => Status >= 400;

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Content);

        public override void Dispose()
        {
        }
    }

    private sealed class TestPipelineResponseHeaders : PipelineResponseHeaders
    {
        public override bool TryGetValue(string name, out string? value)
        {
            value = null;
            return false;
        }

        public override bool TryGetValues(string name, out IEnumerable<string>? values)
        {
            values = null;
            return false;
        }

        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator()
            => Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
    }
}
