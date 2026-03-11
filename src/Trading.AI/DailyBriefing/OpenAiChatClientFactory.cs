using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
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

        return new OpenAIResponseClient(modelId, _options.ApiKey).AsIChatClient();
    }
}
