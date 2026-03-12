using Microsoft.Extensions.AI;

namespace Trading.AI.PromptExecution;

public interface IChatClientFactory
{
    IChatClient CreateClient(string modelId);
}
