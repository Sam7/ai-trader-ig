using Trading.AI.DailyBriefing;
using Trading.AI.Prompts;

namespace Trading.Automation.Execution;

public interface IIntradayOpportunityRequestRenderer
{
    PromptContractProvenance Contract { get; }

    string RenderRequestText(IntradayOpportunityReviewRequest request);
}
