using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.PromptExecution;
using Trading.AI.Prompts;
using Trading.AI.Prompts.IntradayOpportunityReview;
using Trading.Strategy.Shared;

namespace Trading.AI.DailyBriefing;

public sealed class IntradayOpportunityReviewer : IIntradayOpportunityReviewer
{
    private readonly PromptExecutor _promptExecutor;
    private readonly PromptRegistry _promptRegistry;
    private readonly IntradayOpportunityReviewOptions _options;
    private readonly IntradayOpportunityMapper _mapper;

    public IntradayOpportunityReviewer(
        PromptExecutor promptExecutor,
        PromptRegistry promptRegistry,
        IOptions<IntradayOpportunityReviewOptions> options,
        IntradayOpportunityMapper mapper)
    {
        _promptExecutor = promptExecutor;
        _promptRegistry = promptRegistry;
        _options = options.Value;
        _mapper = mapper;
    }

    public PromptContractProvenance Contract
        => _promptRegistry.GetProvenance(PromptRegistry.IntradayOpportunityReview);

    public string RenderRequestText(IntradayOpportunityReviewRequest request)
        => _promptExecutor.RenderRequestText(
            PromptRegistry.IntradayOpportunityReview,
            IntradayOpportunityPromptInputFactory.Create(request));

    public async Task<IntradayOpportunityReviewExecution> ReviewAsync(
        IntradayOpportunityReviewRequest request,
        IReadOnlyList<PromptAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        var input = IntradayOpportunityPromptInputFactory.Create(request);
        var execution = await _promptExecutor.ExecuteStructuredAsync<IntradayOpportunityReviewInput, IntradayOpportunityReviewDocument>(
            PromptRegistry.IntradayOpportunityReview,
            _options,
            input,
            attachments,
            IntradayOpportunityReviewResponseFormat.Create(),
            cancellationToken);

        var reviewedAtUtc = execution.Response.CreatedAt ?? DateTimeOffset.UtcNow;
        var batch = _mapper.Map(
            execution.StructuredValue,
            request.TradingDate,
            request.LookbackStartUtc,
            request.LookbackEndUtc,
            reviewedAtUtc,
            request.MaxCandidatesPerRun);

        return new IntradayOpportunityReviewExecution(
            batch,
            execution.RequestText,
            execution.EnvelopeArtifactPath,
            execution.StructuredArtifactPath,
            execution.AttachmentArtifactPaths);
    }
}
