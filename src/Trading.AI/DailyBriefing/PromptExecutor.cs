using System.Diagnostics;
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

    public PromptExecutor(
        PromptRegistry promptRegistry,
        PromptTemplateRenderer templateRenderer,
        PromptObservabilityWriter observabilityWriter,
        IChatClientFactory chatClientFactory,
        PromptInputConverter inputConverter)
    {
        _promptRegistry = promptRegistry;
        _templateRenderer = templateRenderer;
        _observabilityWriter = observabilityWriter;
        _chatClientFactory = chatClientFactory;
        _inputConverter = inputConverter;
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

        try
        {
            var response = await chatClient.GetResponseAsync(requestMessages, options, cancellationToken);
            await _observabilityWriter.WriteTextAsync(session, response.Text, cancellationToken);

            await _observabilityWriter.CompleteAsync(session, invocation, requestText, requestOptions, response, response.Text, null, stopwatch.Elapsed, cancellationToken);
            return new PromptTextResult(invocation.Prompt.Id, invocation.Prompt.Name, response.ModelId ?? invocation.Model.ModelId, requestText, response, response.Text, session.TextArtifactPath);
        }
        catch (Exception exception)
        {
            await _observabilityWriter.FailAsync(session, invocation, requestText, requestOptions, exception, stopwatch.Elapsed, cancellationToken);
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

            try
            {
                ChatResponse response;
                T structured;

                if (invocation.ResponseFormat is not null)
                {
                    response = await chatClient.GetResponseAsync(requestMessages, options, cancellationToken);
                    structured = DeserializeStructuredResponse<T>(response, invocation.Prompt.Name);
                }
                else
                {
                    var typedResponse = await chatClient.GetResponseAsync<T>(
                        requestMessages,
                        options,
                        useJsonSchemaResponseFormat: true,
                        cancellationToken: cancellationToken);
                    response = typedResponse;
                    if (!typedResponse.TryGetResult(out T? typedStructured) || typedStructured is null)
                    {
                        throw new StructuredOutputException($"Prompt '{invocation.Prompt.Name}' did not return valid structured output.");
                    }

                    structured = typedStructured;
                }

                await _observabilityWriter.WriteStructuredAsync(session, structured!, cancellationToken);
                await _observabilityWriter.CompleteAsync(session, invocation, requestText, requestOptions, response, response.Text, structured!, stopwatch.Elapsed, cancellationToken);
                return new PromptStructuredResult<T>(invocation.Prompt.Id, invocation.Prompt.Name, response.ModelId ?? invocation.Model.ModelId, requestText, response, response.Text, structured, session.StructuredArtifactPath);
            }
            catch (Exception exception) when (attempt < 2 && ShouldRetryStructuredFailure(exception))
            {
                lastException = exception;
                await _observabilityWriter.FailAsync(session, invocation, requestText, requestOptions, exception, stopwatch.Elapsed, cancellationToken);
            }
            catch (Exception exception)
            {
                await _observabilityWriter.FailAsync(session, invocation, requestText, requestOptions, exception, stopwatch.Elapsed, cancellationToken);
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
