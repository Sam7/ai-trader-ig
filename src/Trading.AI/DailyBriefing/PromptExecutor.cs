using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using System.ClientModel;
using Trading.AI.Configuration;
using Trading.AI.Observability;
using Trading.AI.Prompts;

namespace Trading.AI.DailyBriefing;

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

    public PromptExecutor(
        PromptRegistry promptRegistry,
        PromptTemplateRenderer templateRenderer,
        PromptObservabilityWriter observabilityWriter)
    {
        _promptRegistry = promptRegistry;
        _templateRenderer = templateRenderer;
        _observabilityWriter = observabilityWriter;
    }

    public async Task<PromptExecutionResult> ExecuteTextAsync(
        IChatClient chatClient,
        PromptExecutionContext context,
        CancellationToken cancellationToken)
    {
        var promptTemplate = _promptRegistry.GetPromptText(context.Prompt);
        var requestText = _templateRenderer.Render(promptTemplate, context.Variables);
        var options = BuildChatOptions(context.Model, context.ResponseFormat);
        var requestOptions = options.RawRepresentationFactory?.Invoke(chatClient);
        var session = await _observabilityWriter.StartAsync(context, requestText, requestOptions, cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await chatClient.GetResponseAsync(requestText, options, cancellationToken);
            if (context.Prompt == PromptRegistry.DailyBriefResearch)
            {
                await _observabilityWriter.WriteMarkdownAsync(session, response.Text, cancellationToken);
            }

            await _observabilityWriter.CompleteAsync(session, context, requestText, requestOptions, response, response.Text, null, stopwatch.Elapsed, cancellationToken);
            return new PromptExecutionResult(context.Prompt.Id, context.Prompt.Name, response.ModelId ?? context.Model.ModelId, requestText, response, response.Text);
        }
        catch (Exception exception)
        {
            await _observabilityWriter.FailAsync(session, context, requestText, requestOptions, exception, stopwatch.Elapsed, cancellationToken);
            throw;
        }
    }

    public async Task<(PromptExecutionResult Execution, T Structured)> ExecuteStructuredAsync<T>(
        IChatClient chatClient,
        PromptExecutionContext context,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var promptTemplate = _promptRegistry.GetPromptText(context.Prompt);
            var requestText = _templateRenderer.Render(promptTemplate, context.Variables);
            var options = BuildChatOptions(context.Model, context.ResponseFormat);
            var requestOptions = options.RawRepresentationFactory?.Invoke(chatClient);
            var session = await _observabilityWriter.StartAsync(context, requestText, requestOptions, cancellationToken);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                ChatResponse response;
                T? structured;

                if (context.ResponseFormat is not null)
                {
                    response = await chatClient.GetResponseAsync(requestText, options, cancellationToken);
                    structured = JsonSerializer.Deserialize<T>(response.Text, StructuredJsonOptions);
                }
                else
                {
                    var typedResponse = await chatClient.GetResponseAsync<T>(
                        requestText,
                        options,
                        useJsonSchemaResponseFormat: true,
                        cancellationToken: cancellationToken);
                    response = typedResponse;
                    if (!typedResponse.TryGetResult(out structured))
                    {
                        throw new InvalidOperationException($"Prompt '{context.Prompt.Name}' did not return valid structured output.");
                    }
                }

                if (structured is null)
                {
                    throw new InvalidOperationException($"Prompt '{context.Prompt.Name}' did not return valid structured output.");
                }

                await _observabilityWriter.WriteStructuredAsync(session, structured, cancellationToken);
                await _observabilityWriter.CompleteAsync(session, context, requestText, requestOptions, response, response.Text, structured, stopwatch.Elapsed, cancellationToken);
                return (new PromptExecutionResult(context.Prompt.Id, context.Prompt.Name, response.ModelId ?? context.Model.ModelId, requestText, response, response.Text), structured);
            }
            catch (Exception exception) when (attempt < 2 && ShouldRetryStructuredFailure(exception))
            {
                lastException = exception;
                await _observabilityWriter.FailAsync(session, context, requestText, requestOptions, exception, stopwatch.Elapsed, cancellationToken);
            }
            catch (Exception exception)
            {
                await _observabilityWriter.FailAsync(session, context, requestText, requestOptions, exception, stopwatch.Elapsed, cancellationToken);
                throw;
            }
        }

        throw lastException ?? new InvalidOperationException($"Prompt '{context.Prompt.Name}' failed without an error.");
    }

    private static ChatOptions BuildChatOptions(DailyBriefingModelOptions model, ChatResponseFormat? responseFormat)
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
        => exception switch
        {
            ClientResultException => true,
            JsonException => true,
            InvalidOperationException => true,
            _ => false,
        };
}
