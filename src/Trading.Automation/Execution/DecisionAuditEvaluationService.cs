using System.Text.Json;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.Automation.Execution;

public sealed class DecisionAuditEvaluationService : IDecisionAuditEvaluationService
{
    private static readonly TimeSpan AssessmentOutcomeHorizon = TimeSpan.FromHours(1);

    private readonly DecisionAuditWriter _writer;
    private readonly DecisionEvidenceSidecarWriter _sidecarWriter;
    private readonly PaperTradeOutcomeEvaluator _outcomeEvaluator;
    private readonly PaperMarketAssessmentEvaluator _assessmentEvaluator;
    private readonly MarketDataService _marketDataService;
    private readonly AuditMarketDataQualityAnalyzer _dataQualityAnalyzer;

    public DecisionAuditEvaluationService(
        DecisionAuditWriter writer,
        DecisionEvidenceSidecarWriter sidecarWriter,
        PaperTradeOutcomeEvaluator outcomeEvaluator,
        PaperMarketAssessmentEvaluator assessmentEvaluator,
        MarketDataService marketDataService,
        AuditMarketDataQualityAnalyzer dataQualityAnalyzer)
    {
        _writer = writer;
        _sidecarWriter = sidecarWriter;
        _outcomeEvaluator = outcomeEvaluator;
        _assessmentEvaluator = assessmentEvaluator;
        _marketDataService = marketDataService;
        _dataQualityAnalyzer = dataQualityAnalyzer;
    }

    public async Task<DecisionAuditEvaluationReport> EvaluateAsync(
        DecisionAuditEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        var evaluatedAtUtc = DateTimeOffset.UtcNow;
        var files = _writer.FindAuditFiles(request.RootPath, request.TradingDate);
        var evaluatedRecords = new List<DecisionAuditRecord>(files.Count);
        var evaluations = new List<DecisionEvaluationRecord>(files.Count);
        var evaluationArtifacts = new List<ArtifactReference>(files.Count);
        var dataQualityResults = new List<AuditDataQualityResult>();
        var dataQualityPolicy = request.CreateDataQualityPolicy();

        foreach (var file in files)
        {
            var record = await _writer.LoadAsync(file, cancellationToken);
            var assessmentOutcomes = new List<PaperMarketAssessmentOutcome>(record.MarketAssessments.Count);
            var outcomes = new List<PaperTradeOutcome>(record.CandidateOpportunities.Count);
            var recordDataQualityResults = new List<AuditDataQualityResult>();

            foreach (var assessment in record.MarketAssessments)
            {
                var result = await EvaluateAssessmentAsync(record, assessment, request.Resolution, dataQualityPolicy, evaluatedAtUtc, cancellationToken);
                assessmentOutcomes.Add(result.Outcome);
                dataQualityResults.Add(result.DataQuality);
                recordDataQualityResults.Add(result.DataQuality);
            }

            foreach (var candidate in record.CandidateOpportunities)
            {
                var result = await EvaluateCandidateAsync(record, candidate, request.Resolution, dataQualityPolicy, evaluatedAtUtc, cancellationToken);
                outcomes.Add(result.Outcome);
                dataQualityResults.Add(result.DataQuality);
                recordDataQualityResults.Add(result.DataQuality);
            }

            var evaluation = await _sidecarWriter.WriteEvaluationAsync(
                file,
                record,
                evaluatedAtUtc,
                request.Resolution,
                dataQualityPolicy,
                outcomes,
                assessmentOutcomes,
                DecisionAuditDataQualitySummary.From(recordDataQualityResults),
                cancellationToken);
            evaluatedRecords.Add(record);
            evaluations.Add(evaluation.Record);
            evaluationArtifacts.Add(evaluation.Artifact);
        }

        var reportPath = BuildReportPath(request.RootPath, request.TradingDate);
        var report = CreateReport(
            request,
            evaluatedAtUtc,
            evaluatedRecords,
            evaluations,
            ToArtifactReference(reportPath),
            DecisionAuditDataQualitySummary.From(dataQualityResults)) with
        {
            EvaluationArtifacts = evaluationArtifacts,
        };
        await SaveReportAsync(reportPath, report, cancellationToken);
        return report;
    }

    private async Task<(PaperTradeOutcome Outcome, AuditDataQualityResult DataQuality)> EvaluateCandidateAsync(
        DecisionAuditRecord record,
        DecisionAuditCandidate candidate,
        PriceResolution resolution,
        AuditDataQualityPolicy dataQualityPolicy,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken)
    {
        if (candidate.SetupExpiresAtUtc <= record.ReviewedAtUtc)
        {
            var noBars = new AuditDataQualityResult(
                AuditDataQualityUseCase.Candidate,
                AuditDataQualityClassification.NoBars,
                null,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                "Setup expiry was not after the review timestamp.");
            return (CreateCandidateDataInsufficient(candidate, evaluatedAtUtc, 0, noBars.Reason), noBars);
        }

        var interval = GetResolutionInterval(resolution);
        var windowToUtc = candidate.SetupExpiresAtUtc.Add(interval);
        var result = await _marketDataService.GetBarsAsync(
            new MarketDataRequest(
                candidate.Instrument,
                resolution,
                record.ReviewedAtUtc,
                windowToUtc,
                AllowBackfill: false),
            cancellationToken);
        var quality = await _dataQualityAnalyzer.AnalyzeAsync(
            candidate.Instrument,
            resolution,
            record.ReviewedAtUtc,
            windowToUtc,
            AuditDataQualityUseCase.Candidate,
            dataQualityPolicy,
            cancellationToken);

        if (result.Series.Bars.Count == 0)
        {
            return (CreateCandidateDataInsufficient(
                candidate,
                evaluatedAtUtc,
                0,
                FormatAuditDataQualityIssue(quality, "No local market-data bars were available for the setup outcome window.")), quality);
        }

        if (quality.Classification == AuditDataQualityClassification.Complete)
        {
            return (_outcomeEvaluator.Evaluate(candidate, record.ReviewedAtUtc, result.Series, evaluatedAtUtc), quality);
        }

        var tentative = _outcomeEvaluator.Evaluate(candidate, record.ReviewedAtUtc, result.Series, evaluatedAtUtc);
        if (IsDecisiveBeforeFirstIssue(tentative, quality))
        {
            return (tentative with
            {
                Reason = $"{tentative.Reason} Audit accepted the decisive outcome because the first unsafe data-quality issue starts after the close: {quality.FirstIssue!.FromUtc:O}."
            }, quality);
        }

        if (quality.Classification == AuditDataQualityClassification.ClosedMarket
            && quality.FirstIssue is { } closed
            && closed.FromUtc > record.ReviewedAtUtc)
        {
            var clippedExpiry = closed.FromUtc.Subtract(interval);
            if (clippedExpiry >= record.ReviewedAtUtc)
            {
                var clippedCandidate = candidate with { SetupExpiresAtUtc = clippedExpiry };
                var clipped = _outcomeEvaluator.Evaluate(clippedCandidate, record.ReviewedAtUtc, result.Series, evaluatedAtUtc);
                if (clipped.Status != PaperTradeOutcomeStatus.DataInsufficient)
                {
                    return (clipped with
                    {
                        Reason = $"{clipped.Reason} Audit window was clipped at broker closed-market evidence beginning {closed.FromUtc:O}."
                    }, quality);
                }
            }
        }

        return (CreateCandidateDataInsufficient(
            candidate,
            evaluatedAtUtc,
            result.Series.Bars.Count,
            FormatAuditDataQualityIssue(quality, "Local market data was incomplete for the setup outcome window.")), quality);
    }

    private async Task<(PaperMarketAssessmentOutcome Outcome, AuditDataQualityResult DataQuality)> EvaluateAssessmentAsync(
        DecisionAuditRecord record,
        DecisionAuditAssessment assessment,
        PriceResolution resolution,
        AuditDataQualityPolicy dataQualityPolicy,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken)
    {
        var horizonEndsAtUtc = record.ReviewedAtUtc.Add(AssessmentOutcomeHorizon);
        var windowToUtc = horizonEndsAtUtc.Add(GetResolutionInterval(resolution));
        var result = await _marketDataService.GetBarsAsync(
            new MarketDataRequest(
                assessment.Instrument,
                resolution,
                record.ReviewedAtUtc,
                windowToUtc,
                AllowBackfill: false),
            cancellationToken);
        var quality = await _dataQualityAnalyzer.AnalyzeAsync(
            assessment.Instrument,
            resolution,
            record.ReviewedAtUtc,
            windowToUtc,
            AuditDataQualityUseCase.Assessment,
            dataQualityPolicy,
            cancellationToken);

        if (result.Series.Bars.Count == 0)
        {
            return (CreateAssessmentDataInsufficient(
                assessment,
                evaluatedAtUtc,
                horizonEndsAtUtc,
                0,
                FormatAuditDataQualityIssue(quality, "No local market-data bars were available for the assessment horizon.")), quality);
        }

        if (quality.Classification is AuditDataQualityClassification.Complete
            or AuditDataQualityClassification.EvaluatedWithToleratedGaps
            or AuditDataQualityClassification.ClosedMarket)
        {
            var outcome = _assessmentEvaluator.Evaluate(
                assessment,
                record.ReviewedAtUtc,
                horizonEndsAtUtc,
                result.Series,
                evaluatedAtUtc);

            if (outcome.Status != PaperMarketAssessmentOutcomeStatus.DataInsufficient
                && quality.Classification != AuditDataQualityClassification.Complete)
            {
                return (outcome with
                {
                    Reason = $"{outcome.Reason} {quality.Reason} First data-quality issue: {quality.FirstIssue?.FromUtc:O}."
                }, quality);
            }

            return (outcome, quality);
        }

        return (CreateAssessmentDataInsufficient(
            assessment,
            evaluatedAtUtc,
            horizonEndsAtUtc,
            result.Series.Bars.Count,
            FormatAuditDataQualityIssue(quality, "Local market data was incomplete for the assessment horizon.")), quality);
    }

    private static DecisionAuditEvaluationReport CreateReport(
        DecisionAuditEvaluationRequest request,
        DateTimeOffset evaluatedAtUtc,
        IReadOnlyList<DecisionAuditRecord> records,
        IReadOnlyList<DecisionEvaluationRecord> evaluations,
        ArtifactReference reportArtifact,
        DecisionAuditDataQualitySummary dataQuality)
    {
        var allOutcomes = evaluations.SelectMany(record => record.PaperOutcomes).ToArray();
        var allAssessmentOutcomes = evaluations.SelectMany(record => record.MarketAssessmentOutcomes).ToArray();
        var estimatedRValues = allOutcomes
            .Where(outcome => outcome.EstimatedRMultiple is not null)
            .Select(outcome => outcome.EstimatedRMultiple!.Value)
            .ToArray();
        var assessments = records.SelectMany(record => record.MarketAssessments).ToArray();
        var candidates = records.SelectMany(record => record.CandidateOpportunities).ToArray();

        return new DecisionAuditEvaluationReport(
            Path.GetFullPath(request.RootPath),
            request.TradingDate,
            request.Resolution,
            evaluatedAtUtc,
            records.Count,
            allOutcomes.Length,
            allOutcomes.Count(outcome => outcome.Status == PaperTradeOutcomeStatus.TargetHit),
            allOutcomes.Count(outcome => outcome.Status == PaperTradeOutcomeStatus.StoppedOut),
            allOutcomes.Count(outcome => outcome.Status == PaperTradeOutcomeStatus.Expired),
            allOutcomes.Count(outcome => outcome.Status == PaperTradeOutcomeStatus.NoFill),
            allOutcomes.Count(outcome => outcome.Status == PaperTradeOutcomeStatus.DataInsufficient),
            allAssessmentOutcomes.Length,
            allAssessmentOutcomes.Count(outcome => outcome.Status == PaperMarketAssessmentOutcomeStatus.FollowedBias),
            allAssessmentOutcomes.Count(outcome => outcome.Status == PaperMarketAssessmentOutcomeStatus.MovedAgainstBias),
            allAssessmentOutcomes.Count(outcome => outcome.Status == PaperMarketAssessmentOutcomeStatus.Flat),
            allAssessmentOutcomes.Count(outcome => outcome.Status == PaperMarketAssessmentOutcomeStatus.DataInsufficient),
            estimatedRValues.Length == 0 ? null : estimatedRValues.Average(),
            DecisionBiasSummary.From(assessments, candidates),
            reportArtifact,
            dataQuality);
    }

    private static string BuildReportPath(string rootPath, DateOnly? tradingDate)
    {
        var root = Path.GetFullPath(rootPath);
        var directory = tradingDate is null
            ? root
            : Path.Combine(root, tradingDate.Value.ToString("yyyy-MM-dd"));
        return Path.Combine(directory, "decision-audit-summary.json");
    }

    private static async Task SaveReportAsync(
        string path,
        DecisionAuditEvaluationReport report,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, DecisionAuditJson.Options), cancellationToken);
    }

    private static TimeSpan GetResolutionInterval(PriceResolution resolution)
        => resolution switch
        {
            PriceResolution.Second => TimeSpan.FromSeconds(1),
            PriceResolution.Minute => TimeSpan.FromMinutes(1),
            PriceResolution.TwoMinutes => TimeSpan.FromMinutes(2),
            PriceResolution.ThreeMinutes => TimeSpan.FromMinutes(3),
            PriceResolution.FiveMinutes => TimeSpan.FromMinutes(5),
            PriceResolution.TenMinutes => TimeSpan.FromMinutes(10),
            PriceResolution.FifteenMinutes => TimeSpan.FromMinutes(15),
            PriceResolution.ThirtyMinutes => TimeSpan.FromMinutes(30),
            PriceResolution.Hour => TimeSpan.FromHours(1),
            PriceResolution.TwoHours => TimeSpan.FromHours(2),
            PriceResolution.ThreeHours => TimeSpan.FromHours(3),
            PriceResolution.FourHours => TimeSpan.FromHours(4),
            PriceResolution.Day => TimeSpan.FromDays(1),
            PriceResolution.Week => TimeSpan.FromDays(7),
            PriceResolution.Month => TimeSpan.FromDays(31),
            _ => TimeSpan.FromMinutes(5),
        };

    private static decimal? CalculateSpreadCostR(DecisionAuditCandidate candidate)
    {
        var risk = Math.Abs(candidate.EntryPrice - candidate.StopLossPrice);
        return risk > 0m ? candidate.CurrentSpread / risk : null;
    }

    private static bool IsDecisiveBeforeFirstIssue(
        PaperTradeOutcome outcome,
        AuditDataQualityResult quality)
        => outcome.Status is PaperTradeOutcomeStatus.TargetHit or PaperTradeOutcomeStatus.StoppedOut
            && outcome.ClosedAtUtc is DateTimeOffset closedAtUtc
            && quality.FirstIssue is { } firstIssue
            && closedAtUtc < firstIssue.FromUtc;

    private static PaperTradeOutcome CreateCandidateDataInsufficient(
        DecisionAuditCandidate candidate,
        DateTimeOffset evaluatedAtUtc,
        int barsEvaluated,
        string reason)
        => new(
            candidate.Instrument,
            candidate.Direction,
            PaperTradeOutcomeStatus.DataInsufficient,
            evaluatedAtUtc,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            candidate.CurrentSpread,
            CalculateSpreadCostR(candidate),
            barsEvaluated,
            reason);

    private static PaperMarketAssessmentOutcome CreateAssessmentDataInsufficient(
        DecisionAuditAssessment assessment,
        DateTimeOffset evaluatedAtUtc,
        DateTimeOffset horizonEndsAtUtc,
        int barsEvaluated,
        string reason)
        => new(
            assessment.Instrument,
            assessment.DirectionalBias,
            PaperMarketAssessmentOutcomeStatus.DataInsufficient,
            evaluatedAtUtc,
            horizonEndsAtUtc,
            null,
            null,
            null,
            null,
            null,
            null,
            barsEvaluated,
            reason);

    private static string FormatAuditDataQualityIssue(AuditDataQualityResult quality, string fallback)
    {
        if (quality.FirstIssue is not { } firstIssue)
        {
            return $"{fallback} {quality.Reason}";
        }

        return $"{fallback} {quality.Reason} First missing range: {firstIssue.FromUtc:O} to {firstIssue.ToUtc:O}.";
    }

    private static ArtifactReference ToArtifactReference(string path)
        => new(Path.GetFullPath(path), new Uri(Path.GetFullPath(path)).AbsoluteUri);
}
