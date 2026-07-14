using Trading.AI.PromptExecution;
using Trading.AI.Prompts;

namespace Trading.AI.DailyBriefing;

public interface IIntradayOpportunityReviewer
{
    PromptContractProvenance Contract { get; }

    string RenderRequestText(IntradayOpportunityReviewRequest request);

    Task<IntradayOpportunityReviewExecution> ReviewAsync(
        IntradayOpportunityReviewRequest request,
        IReadOnlyList<PromptAttachment> attachments,
        CancellationToken cancellationToken = default);
}
