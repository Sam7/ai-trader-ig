using System.Diagnostics;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Trading.AI.Configuration;
using Trading.AI.Observability;
using Trading.AI.Prompts;

namespace Trading.AI.DailyBriefing;

public sealed class PromptExecutor
{
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
        var options = BuildChatOptions(context.Model);
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
        var promptTemplate = _promptRegistry.GetPromptText(context.Prompt);
        var requestText = _templateRenderer.Render(promptTemplate, context.Variables);
        var options = BuildChatOptions(context.Model);
        var requestOptions = options.RawRepresentationFactory?.Invoke(chatClient);
        var session = await _observabilityWriter.StartAsync(context, requestText, requestOptions, cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await chatClient.GetResponseAsync<T>(requestText, options, cancellationToken: cancellationToken);
            if (!response.TryGetResult(out var structured))
            {
                throw new InvalidOperationException($"Prompt '{context.Prompt.Name}' did not return valid structured output.");
            }

            await _observabilityWriter.CompleteAsync(session, context, requestText, requestOptions, response, response.Text, structured, stopwatch.Elapsed, cancellationToken);
            return (new PromptExecutionResult(context.Prompt.Id, context.Prompt.Name, response.ModelId ?? context.Model.ModelId, requestText, response, response.Text), structured);
        }
        catch (Exception exception)
        {
            await _observabilityWriter.FailAsync(session, context, requestText, requestOptions, exception, stopwatch.Elapsed, cancellationToken);
            throw;
        }
    }

    private static ChatOptions BuildChatOptions(DailyBriefingModelOptions model)
    {
        var options = new ChatOptions
        {
            ModelId = model.ModelId,
            Temperature = model.Temperature is null ? null : (float)model.Temperature.Value,
            MaxOutputTokens = model.MaxOutputTokens,
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
}
