using Microsoft.Extensions.AI;

namespace Trading.AI.DailyBriefing;

public interface IChatClientFactory
{
    IChatClient CreateClient(string modelId);
}
