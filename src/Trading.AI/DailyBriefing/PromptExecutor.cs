using System.Diagnostics;
using System.ClientModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Trading.AI.Configuration;
using Trading.AI.DailyBriefing;
using Trading.AI.Observability;
using Trading.AI.Prompts;
using OpenAI.Responses;

namespace Trading.AI.PromptExecution;

public sealed class PromptExecutor
{
    private const int MaxTransientAttempts = 3;

    private static readonly TimeSpan[] TransientRetryDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(8),
    ];

    private static readonly JsonSerializerOptions StructuredJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly PromptRegistry _promptRegistry;
    private readonly PromptTemplateRenderer _templateRenderer;
    private readonly PromptObservabilityWriter _observabilityWriter;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly PromptInputConverter _inputConverter;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    public PromptExecutor(
        PromptRegistry promptRegistry,
        PromptTemplateRenderer templateRenderer,
        PromptObservabilityWriter observabilityWriter,
        IChatClientFactory chatClientFactory,
        PromptInputConverter inputConverter,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _promptRegistry = promptRegistry;
        _templateRenderer = templateRenderer;
        _observabilityWriter = observabilityWriter;
        _chatClientFactory = chatClientFactory;
        _inputConverter = inputConverter;
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public Task<PromptTextResult> ExecuteTextAsync(
        PromptDefinition prompt,
        PromptModelOptions model,
        IReadOnlyDictionary<string, string> variables,
        PromptTextArtifactKind artifactKind = PromptTextArtifactKind.Markdown,
        CancellationToken cancellationToken = default)
        => ExecuteTextCoreAsync(CreateInvocation(prompt, model, _inputConverter.Convert(variables), null, artifactKind, []), cancellationToken);

    public Task<PromptTextResult> ExecuteTextAsync<TInput>(
        PromptDefinition prompt,
        PromptModelOptions model,
        TInput input,
        PromptTextArtifactKind artifactKind = PromptTextArtifactKind.Markdown,
        CancellationToken cancellationToken = default)
        => ExecuteTextCoreAsync(CreateInvocation(prompt, model, _inputConverter.Convert(input), null, artifactKind, []), cancellationToken);

    public Task<PromptStructuredResult<T>> ExecuteStructuredAsync<T>(
        PromptDefinition prompt,
        PromptModelOptions model,
        IReadOnlyDictionary<string, string> variables,
        ChatResponseFormat? responseFormat = null,
        CancellationToken cancellationToken = default)
        => ExecuteStructuredCoreAsync<T>(CreateInvocation(prompt, model, _inputConverter.Convert(variables), responseFormat, PromptTextArtifactKind.None, []), cancellationToken);

    public Task<PromptStructuredResult<TResult>> ExecuteStructuredAsync<TInput, TResult>(
        PromptDefinition prompt,
        PromptModelOptions model,
        TInput input,
        ChatResponseFormat? responseFormat = null,
        CancellationToken cancellationToken = default)
        => ExecuteStructuredCoreAsync<TResult>(CreateInvocation(prompt, model, _inputConverter.Convert(input), responseFormat, PromptTextArtifactKind.None, []), cancellationToken);

    public Task<PromptStructuredResult<TResult>> ExecuteStructuredAsync<TInput, TResult>(
        PromptDefinition prompt,
        PromptModelOptions model,
        TInput input,
        IReadOnlyList<PromptAttachment> attachments,
        ChatResponseFormat? responseFormat = null,
        CancellationToken cancellationToken = default)
        => ExecuteStructuredCoreAsync<TResult>(CreateInvocation(prompt, model, _inputConverter.Convert(input), responseFormat, PromptTextArtifactKind.None, attachments), cancellationToken);

    public string RenderRequestText<TInput>(PromptDefinition prompt, TInput input)
    {
        var promptTemplate = _promptRegistry.GetPromptText(prompt);
        return _templateRenderer.Render(promptTemplate, _inputConverter.Convert(input).Variables);
    }

    private async Task<PromptTextResult> ExecuteTextCoreAsync(
        PromptInvocation invocation,
        CancellationToken cancellationToken)
    {
        var promptTemplate = _promptRegistry.GetPromptText(invocation.Prompt);
        var requestText = _templateRenderer.Render(promptTemplate, invocation.Variables);
        var requestMessages = BuildRequestMessages(requestText, invocation.Attachments);
        using var chatClient = _chatClientFactory.CreateClient(invocation.Model.ModelId);
        var options = BuildChatOptions(invocation.Model, invocation.ResponseFormat);
        var requestOptions = options.RawRepresentationFactory?.Invoke(chatClient);
        var session = await _observabilityWriter.StartAsync(invocation, requestText, requestOptions, cancellationToken);
        await _observabilityWriter.WriteAttachmentsAsync(session, invocation.Attachments, cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        var attempts = new List<PromptAttemptRecord>();

        try
        {
            var response = await ExecuteProviderCallWithTransientRetryAsync(
                token => chatClient.GetResponseAsync(requestMessages, options, token),
                attempts,
                cancellationToken);
            await _observabilityWriter.WriteTextAsync(session, response.Text, cancellationToken);

            await _observabilityWriter.CompleteAsync(session, invocation, requestText, requestOptions, response, response.Text, null, stopwatch.Elapsed, cancellationToken, attempts);
            return new PromptTextResult(
                invocation.Prompt.Id,
                invocation.Prompt.Name,
                response.ModelId ?? invocation.Model.ModelId,
                requestText,
                response,
                response.Text,
                session.TextArtifactPath,
                session.JsonPath,
                session.AttachmentArtifactPaths.AsReadOnly());
        }
        catch (Exception exception)
        {
            await _observabilityWriter.FailAsync(session, invocation, requestText, requestOptions, exception, stopwatch.Elapsed, cancellationToken, attempts);
            throw;
        }
    }

    private async Task<PromptStructuredResult<T>> ExecuteStructuredCoreAsync<T>(
        PromptInvocation invocation,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var promptTemplate = _promptRegistry.GetPromptText(invocation.Prompt);
            var requestText = _templateRenderer.Render(promptTemplate, invocation.Variables);
            var requestMessages = BuildRequestMessages(requestText, invocation.Attachments);
            using var chatClient = _chatClientFactory.CreateClient(invocation.Model.ModelId);
            var options = BuildChatOptions(invocation.Model, invocation.ResponseFormat);
            var requestOptions = options.RawRepresentationFactory?.Invoke(chatClient);
            var session = await _observabilityWriter.StartAsync(invocation, requestText, requestOptions, cancellationToken);
            await _observabilityWriter.WriteAttachmentsAsync(session, invocation.Attachments, cancellationToken);
            var stopwatch = Stopwatch.StartNew();
            var attempts = new List<PromptAttemptRecord>();

            try
            {
                ChatResponse response;
                T structured;

                if (invocation.ResponseFormat is not null)
                {
                    response = await ExecuteProviderCallWithTransientRetryAsync(
                        token => chatClient.GetResponseAsync(requestMessages, options, token),
                        attempts,
                        cancellationToken);
                    structured = DeserializeStructuredResponse<T>(response, invocation.Prompt.Name);
                }
                else
                {
                    var typedResponse = await ExecuteProviderCallWithTransientRetryAsync(
                        token => chatClient.GetResponseAsync<T>(
                            requestMessages,
                            options,
                            useJsonSchemaResponseFormat: true,
                            cancellationToken: token),
                        attempts,
                        cancellationToken);
                    response = typedResponse;
                    if (!typedResponse.TryGetResult(out T? typedStructured) || typedStructured is null)
                    {
                        throw new StructuredOutputException($"Prompt '{invocation.Prompt.Name}' did not return valid structured output.");
                    }

                    structured = typedStructured;
                }

                await _observabilityWriter.WriteStructuredAsync(session, structured!, cancellationToken);
                await _observabilityWriter.CompleteAsync(session, invocation, requestText, requestOptions, response, response.Text, structured!, stopwatch.Elapsed, cancellationToken, attempts);
                return new PromptStructuredResult<T>(
                    invocation.Prompt.Id,
                    invocation.Prompt.Name,
                    response.ModelId ?? invocation.Model.ModelId,
                    requestText,
                    response,
                    response.Text,
                    structured,
                    session.StructuredArtifactPath,
                    session.JsonPath,
                    session.AttachmentArtifactPaths.AsReadOnly());
            }
            catch (Exception exception) when (attempt < 2 && ShouldRetryStructuredFailure(exception))
            {
                lastException = exception;
                await _observabilityWriter.FailAsync(session, invocation, requestText, requestOptions, exception, stopwatch.Elapsed, cancellationToken, attempts);
            }
            catch (Exception exception)
            {
                await _observabilityWriter.FailAsync(session, invocation, requestText, requestOptions, exception, stopwatch.Elapsed, cancellationToken, attempts);
                throw;
            }
        }

        throw lastException ?? new InvalidOperationException($"Prompt '{invocation.Prompt.Name}' failed without an error.");
    }

    private static PromptInvocation CreateInvocation(
        PromptDefinition prompt,
        PromptModelOptions model,
        PromptInputData input,
        ChatResponseFormat? responseFormat,
        PromptTextArtifactKind textArtifactKind,
        IReadOnlyList<PromptAttachment> attachments)
        => new(
            prompt,
            model,
            input.Variables,
            input.PromptDate,
            input.RequestedAtUtc,
            responseFormat,
            textArtifactKind,
            attachments);

    private static ChatOptions BuildChatOptions(PromptModelOptions model, ChatResponseFormat? responseFormat)
    {
        var options = new ChatOptions
        {
            ModelId = model.ModelId,
            Temperature = model.Temperature is null ? null : (float)model.Temperature.Value,
            MaxOutputTokens = model.MaxOutputTokens,
            ResponseFormat = responseFormat,
        };

        if (model.EnableWebSearch)
        {
            options.RawRepresentationFactory = _ => new ResponseCreationOptions
            {
                Tools = { ResponseTool.CreateWebSearchTool() },
            };
        }

        return options;
    }

    private static bool ShouldRetryStructuredFailure(Exception exception)
        => exception is StructuredOutputException;

    private async Task<T> ExecuteProviderCallWithTransientRetryAsync<T>(
        Func<CancellationToken, Task<T>> action,
        List<PromptAttemptRecord> attempts,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxTransientAttempts; attempt++)
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            try
            {
                var result = await action(cancellationToken);
                attempts.Add(new PromptAttemptRecord(
                    attempt,
                    startedAtUtc,
                    DateTimeOffset.UtcNow,
                    "Completed",
                    null,
                    null,
                    null));
                return result;
            }
            catch (Exception exception) when (ShouldRetryTransientProviderFailure(exception, attempt))
            {
                attempts.Add(CreateFailedAttempt(attempt, startedAtUtc, exception));
                await _delayAsync(GetTransientRetryDelay(attempt), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                attempts.Add(CreateFailedAttempt(attempt, startedAtUtc, exception));
                throw;
            }
        }

        throw new InvalidOperationException("Transient provider retry loop exited without a result or exception.");
    }

    private static PromptAttemptRecord CreateFailedAttempt(int attempt, DateTimeOffset startedAtUtc, Exception exception)
        => new(
            attempt,
            startedAtUtc,
            DateTimeOffset.UtcNow,
            "Failed",
            ResolveHttpStatus(exception),
            exception.GetType().FullName,
            exception.Message);

    private static bool ShouldRetryTransientProviderFailure(Exception exception, int attempt)
        => attempt < MaxTransientAttempts && IsTransientProviderFailure(exception);

    private static bool IsTransientProviderFailure(Exception exception)
        => exception switch
        {
            ClientResultException clientException => IsTransientHttpStatus(clientException.Status),
            HttpRequestException => true,
            _ => false,
        };

    private static bool IsTransientHttpStatus(int status)
        => status is 500 or 502 or 503 or 504 or 520 or 522 or 524;

    private static int? ResolveHttpStatus(Exception exception)
        => exception is ClientResultException clientException ? clientException.Status : null;

    private static TimeSpan GetTransientRetryDelay(int attempt)
    {
        var baseDelay = TransientRetryDelays[Math.Min(attempt - 1, TransientRetryDelays.Length - 1)];
        return baseDelay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
    }

    private static IReadOnlyList<ChatMessage> BuildRequestMessages(string requestText, IReadOnlyList<PromptAttachment> attachments)
    {
        if (attachments.Count == 0)
        {
            return [new ChatMessage(ChatRole.User, requestText)];
        }

        var contents = new List<AIContent>(1 + (attachments.Count * 2))
        {
            new TextContent(requestText)
        };

        foreach (var attachment in attachments)
        {
            contents.Add(new TextContent($"Attachment: {attachment.Label}"));
            contents.Add(new DataContent(attachment.Data, attachment.MediaType));
        }

        return [new ChatMessage(ChatRole.User, contents)];
    }

    private static T DeserializeStructuredResponse<T>(ChatResponse response, string promptName)
    {
        if (string.IsNullOrWhiteSpace(response.Text))
        {
            throw new StructuredOutputException($"Prompt '{promptName}' returned an empty structured response.");
        }

        try
        {
            var structured = JsonSerializer.Deserialize<T>(response.Text, StructuredJsonOptions);
            return structured ?? throw new StructuredOutputException($"Prompt '{promptName}' did not return valid structured output.");
        }
        catch (JsonException exception)
        {
            throw new StructuredOutputException($"Prompt '{promptName}' returned invalid JSON.", exception);
        }
    }
}
