using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Responses;
using Trading.AI.Configuration;

namespace Trading.AI.PromptExecution;

internal sealed class OpenAiBackgroundResponseClient : IBackgroundResponseClient
{
    private readonly OpenAiConnectionOptions _options;

    public OpenAiBackgroundResponseClient(IOptions<OpenAiConnectionOptions> options)
    {
        _options = options.Value;
    }

    public async Task<BackgroundResponseResult> CreateAsync(
        PromptInvocation invocation,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken)
    {
        var responseClient = CreateResponseClient(invocation.Model.ModelId);
        var responseOptions = BuildResponseOptions(invocation, options);
        responseOptions.BackgroundModeEnabled = true;
        responseOptions.StoredOutputEnabled = true;

        var response = await responseClient.CreateResponseAsync(
            ConvertMessages(messages),
            responseOptions,
            cancellationToken);

        return Convert(response.Value, responseOptions);
    }

    public async Task<BackgroundResponseResult> GetAsync(
        string modelId,
        string responseId,
        ChatOptions options,
        CancellationToken cancellationToken)
    {
        var responseClient = CreateResponseClient(modelId);
        var response = await responseClient.GetResponseAsync(responseId, cancellationToken);
        var responseOptions = BuildResponseOptions(options);
        return Convert(response.Value, responseOptions);
    }

    public async Task CancelAsync(
        string modelId,
        string responseId,
        CancellationToken cancellationToken)
    {
        var responseClient = CreateResponseClient(modelId);
        await responseClient.CancelResponseAsync(responseId, cancellationToken);
    }

    private OpenAIResponseClient CreateResponseClient(string modelId)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("OpenAI API key is not configured.");
        }

        var clientOptions = new OpenAIClientOptions
        {
            NetworkTimeout = _options.RequestTimeout,
            RetryPolicy = new ClientRetryPolicy(0),
        };

        return new OpenAIResponseClient(modelId, new ApiKeyCredential(_options.ApiKey), clientOptions);
    }

    private static ResponseCreationOptions BuildResponseOptions(
        PromptInvocation invocation,
        ChatOptions options)
    {
        var responseOptions = BuildResponseOptions(options);
        if (invocation.Model.EnableWebSearch)
        {
            responseOptions.Tools.Add(ResponseTool.CreateWebSearchTool());
        }

        return responseOptions;
    }

    private static ResponseCreationOptions BuildResponseOptions(ChatOptions options)
    {
        var responseOptions = new ResponseCreationOptions
        {
            Temperature = options.Temperature,
            MaxOutputTokenCount = options.MaxOutputTokens,
        };

        if (options.ResponseFormat is ChatResponseFormatJson jsonFormat)
        {
            responseOptions.TextOptions = new ResponseTextOptions
            {
                TextFormat = jsonFormat.Schema is { } schema
                    ? ResponseTextFormat.CreateJsonSchemaFormat(
                        string.IsNullOrWhiteSpace(jsonFormat.SchemaName) ? "structured_response" : jsonFormat.SchemaName,
                        BinaryData.FromString(schema.GetRawText()),
                        jsonFormat.SchemaDescription,
                        true)
                    : ResponseTextFormat.CreateJsonObjectFormat(),
            };
        }

        return responseOptions;
    }

    private static IReadOnlyList<ResponseItem> ConvertMessages(IReadOnlyList<ChatMessage> messages)
    {
        var items = new List<ResponseItem>(messages.Count);
        foreach (var message in messages)
        {
            var parts = ConvertContents(message.Contents);
            if (message.Role == ChatRole.User)
            {
                items.Add(ResponseItem.CreateUserMessageItem(parts));
                continue;
            }

            if (message.Role == ChatRole.System)
            {
                items.Add(ResponseItem.CreateSystemMessageItem(parts));
                continue;
            }

            if (message.Role == ChatRole.Assistant)
            {
                items.Add(ResponseItem.CreateAssistantMessageItem(parts));
                continue;
            }

            throw new NotSupportedException($"Chat role '{message.Role}' is not supported by background Responses execution.");
        }

        return items;
    }

    private static IReadOnlyList<ResponseContentPart> ConvertContents(IList<AIContent> contents)
    {
        var parts = new List<ResponseContentPart>(contents.Count);
        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent text:
                    parts.Add(ResponseContentPart.CreateInputTextPart(text.Text));
                    break;
                case DataContent data when data.HasTopLevelMediaType("image"):
                    parts.Add(ResponseContentPart.CreateInputImagePart(
                        BinaryData.FromBytes(data.Data),
                        data.MediaType,
                        ResponseImageDetailLevel.Auto));
                    break;
                default:
                    throw new NotSupportedException($"AI content type '{content.GetType().Name}' is not supported by background Responses execution.");
            }
        }

        return parts;
    }

    private static BackgroundResponseResult Convert(OpenAIResponse response, ResponseCreationOptions options)
    {
        var status = response.Status?.ToString() ?? string.Empty;
        return new BackgroundResponseResult(
            response.Id,
            status,
            IsCompletedStatus(status) ? response.AsChatResponse(options) : null,
            response.Error?.Message ?? response.IncompleteStatusDetails?.Reason?.ToString());
    }

    private static bool IsCompletedStatus(string status)
        => status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
}
