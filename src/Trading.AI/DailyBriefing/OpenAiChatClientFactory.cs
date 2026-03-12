using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.ClientModel;
using OpenAI;
using OpenAI.Responses;
using Trading.AI.Configuration;

namespace Trading.AI.DailyBriefing;

public sealed class OpenAiChatClientFactory : IChatClientFactory
{
    private readonly OpenAiConnectionOptions _options;

    public OpenAiChatClientFactory(IOptions<OpenAiConnectionOptions> options)
    {
        _options = options.Value;
    }

    public IChatClient CreateClient(string modelId)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("OpenAI API key is not configured.");
        }

        var clientOptions = new OpenAIClientOptions
        {
            NetworkTimeout = _options.RequestTimeout,
        };

        return new OpenAIResponseClient(modelId, new ApiKeyCredential(_options.ApiKey), clientOptions).AsIChatClient();
    }
}
