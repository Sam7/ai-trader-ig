using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.PromptExecution;
using Trading.AI.Prompts;
using Trading.AI.Prompts.IntradayOpportunityReview;
using Trading.Strategy.Shared;

namespace Trading.AI.DailyBriefing;

public sealed class IntradayOpportunityReviewer
{
    private readonly PromptExecutor _promptExecutor;
    private readonly IntradayOpportunityReviewOptions _options;
    private readonly IntradayOpportunityMapper _mapper;

    public IntradayOpportunityReviewer(
        PromptExecutor promptExecutor,
        IOptions<IntradayOpportunityReviewOptions> options,
        IntradayOpportunityMapper mapper)
    {
        _promptExecutor = promptExecutor;
        _options = options.Value;
        _mapper = mapper;
    }

    public async Task<IntradayOpportunityBatch> ReviewAsync(
        IntradayOpportunityReviewInput input,
        IReadOnlyList<PromptAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        var execution = await _promptExecutor.ExecuteStructuredAsync<IntradayOpportunityReviewInput, IntradayOpportunityReviewDocument>(
            PromptRegistry.IntradayOpportunityReview,
            _options,
            input,
            attachments,
            IntradayOpportunityReviewResponseFormat.Create(),
            cancellationToken);

        var reviewedAtUtc = execution.Response.CreatedAt ?? DateTimeOffset.UtcNow;
        return _mapper.Map(
            execution.StructuredValue,
            input.TradingDate,
            input.LookbackStartUtc,
            input.LookbackEndUtc,
            reviewedAtUtc,
            input.MaxCandidatesPerRun);
    }
}
