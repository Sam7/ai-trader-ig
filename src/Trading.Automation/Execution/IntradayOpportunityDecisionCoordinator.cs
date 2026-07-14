using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.Abstractions;
using Trading.AI.DailyBriefing;
using Trading.Automation.Configuration;
using Trading.Execution;
using Trading.Strategy.OpportunityReview;
using Trading.Strategy.Shared;

namespace Trading.Automation.Execution;

public sealed class IntradayOpportunityDecisionCoordinator : IIntradayOpportunityDecisionCoordinator
{
    private readonly IIntradayDecisionService _decisionService;
    private readonly DecisionAuditWriter _decisionAuditWriter;
    private readonly ExecutionBoundaryService _executionBoundaryService;
    private readonly DemoCanaryExecutionService _demoCanaryExecutionService;
    private readonly AutomationOptions _options;
    private readonly ILogger<IntradayOpportunityDecisionCoordinator> _logger;

    public IntradayOpportunityDecisionCoordinator(
        IIntradayDecisionService decisionService,
        DecisionAuditWriter decisionAuditWriter,
        ExecutionBoundaryService executionBoundaryService,
        DemoCanaryExecutionService demoCanaryExecutionService,
        IOptions<AutomationOptions> options,
        ILogger<IntradayOpportunityDecisionCoordinator> logger)
    {
        _decisionService = decisionService;
        _decisionAuditWriter = decisionAuditWriter;
        _executionBoundaryService = executionBoundaryService;
        _demoCanaryExecutionService = demoCanaryExecutionService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IntradayOpportunitySubmitResult> CoordinateAsync(
        IntradayOpportunityPreparationDocument prepared,
        IntradayOpportunityReviewExecution execution,
        CancellationToken cancellationToken = default)
    {
        var auditId = _decisionAuditWriter.CreateAuditId(prepared.TradingDate, prepared.RequestedAtUtc);
        var batch = execution.Batch with
        {
            MarketQuotes = prepared.Markets
                .Select(market => new IntradayMarketQuote(
                    new InstrumentId(market.InstrumentId),
                    market.CurrentPrice,
                    market.CurrentSpread,
                    market.LatestBarAtUtc))
                .ToArray(),
            SourceDecisionAuditId = auditId,
        };
        var reviewResult = await _decisionService.ReviewAsync(batch, cancellationToken);
        _logger.LogInformation(
            "Validated intraday opportunity batch for {TradingDate}. Assessments: {AssessmentCount}. Candidates: {CandidateCount}. Outcome: {Outcome}",
            reviewResult.TradingDate,
            reviewResult.MarketAssessments.Count,
            reviewResult.CandidateOpportunities.Count,
            reviewResult.Outcome);

        ExecutionBoundaryRecord? executionBoundaryRecord = null;
        if (reviewResult.SelectedShadowIntent is { } selectedIntent)
        {
            var reservation = await _executionBoundaryService.ReserveAsync(selectedIntent, cancellationToken);
            executionBoundaryRecord = reservation.Record;
            _logger.LogInformation(
                "Reserved execution boundary for decision {DecisionId}. Created: {Created}. State: {State}. Deal reference: {DealReference}.",
                executionBoundaryRecord.DecisionId,
                reservation.Created,
                executionBoundaryRecord.State,
                executionBoundaryRecord.DealReference);
        }

        var executionArtifacts = new IntradayOpportunityExecutionArtifacts(
            ToArtifactReference(execution.EnvelopeArtifactPath),
            ToArtifactReference(execution.StructuredArtifactPath),
            execution.AttachmentArtifactPaths.Select(ToArtifactReference).ToArray());
        var decisionAuditArtifact = await _decisionAuditWriter.WriteInitialAsync(
            prepared,
            executionArtifacts,
            reviewResult,
            executionBoundaryRecord is null ? null : ExecutionBoundarySnapshot.From(executionBoundaryRecord),
            cancellationToken);
        if (executionBoundaryRecord is not null)
        {
            executionBoundaryRecord = await _executionBoundaryService.AttachDecisionAuditArtifactAsync(
                executionBoundaryRecord.DecisionId,
                decisionAuditArtifact.Path,
                cancellationToken) ?? executionBoundaryRecord;
        }

        var result = new IntradayOpportunitySubmitResult(
            prepared,
            executionArtifacts with { DecisionAuditArtifact = decisionAuditArtifact },
            batch,
            reviewResult,
            executionBoundaryRecord is null ? null : ExecutionBoundarySnapshot.From(executionBoundaryRecord));

        if (_options.Execution.Mode == TradingExecutionMode.Demo
            && reviewResult.SelectedShadowIntent is not null)
        {
            var demoExecution = await _demoCanaryExecutionService.ExecuteAsync(result, cancellationToken);
            result = result with { DemoExecution = demoExecution };
            _logger.LogInformation(
                "Demo canary execution completed for decision {DecisionId}. Outcome: {Outcome}.",
                demoExecution?.DecisionId ?? reviewResult.SelectedShadowIntent.DecisionId,
                demoExecution?.Outcome ?? "n/a");
        }

        _logger.LogInformation(
            "Submitted intraday opportunity review for {TradingDate}. Envelope: {EnvelopePath}. Extracted JSON: {StructuredPath}. Decision audit: {DecisionAuditPath}.",
            prepared.TradingDate,
            result.ExecutionArtifacts.PromptEnvelopeArtifact.Path,
            result.ExecutionArtifacts.ExtractedJsonArtifact.Path,
            result.ExecutionArtifacts.DecisionAuditArtifact?.Path);
        return result;
    }

    private static ArtifactReference ToArtifactReference(string path)
        => new(Path.GetFullPath(path), new Uri(Path.GetFullPath(path)).AbsoluteUri);
}
