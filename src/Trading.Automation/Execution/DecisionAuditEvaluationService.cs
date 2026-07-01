using System.Text.Json;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.Automation.Execution;

public sealed class DecisionAuditEvaluationService : IDecisionAuditEvaluationService
{
    private static readonly TimeSpan AssessmentOutcomeHorizon = TimeSpan.FromHours(1);

    private readonly DecisionAuditWriter _writer;
    private readonly PaperTradeOutcomeEvaluator _outcomeEvaluator;
    private readonly PaperMarketAssessmentEvaluator _assessmentEvaluator;
    private readonly MarketDataService _marketDataService;

    public DecisionAuditEvaluationService(
        DecisionAuditWriter writer,
        PaperTradeOutcomeEvaluator outcomeEvaluator,
        PaperMarketAssessmentEvaluator assessmentEvaluator,
        MarketDataService marketDataService)
    {
        _writer = writer;
        _outcomeEvaluator = outcomeEvaluator;
        _assessmentEvaluator = assessmentEvaluator;
        _marketDataService = marketDataService;
    }

    public async Task<DecisionAuditEvaluationReport> EvaluateAsync(
        DecisionAuditEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        var evaluatedAtUtc = DateTimeOffset.UtcNow;
        var files = _writer.FindAuditFiles(request.RootPath, request.TradingDate);
        var evaluatedRecords = new List<DecisionAuditRecord>(files.Count);

        foreach (var file in files)
        {
            var record = await _writer.LoadAsync(file, cancellationToken);
            var assessmentOutcomes = new List<PaperMarketAssessmentOutcome>(record.MarketAssessments.Count);
            var outcomes = new List<PaperTradeOutcome>(record.CandidateOpportunities.Count);

            foreach (var assessment in record.MarketAssessments)
            {
                assessmentOutcomes.Add(await EvaluateAssessmentAsync(record, assessment, request.Resolution, evaluatedAtUtc, cancellationToken));
            }

            foreach (var candidate in record.CandidateOpportunities)
            {
                outcomes.Add(await EvaluateCandidateAsync(record, candidate, request.Resolution, evaluatedAtUtc, cancellationToken));
            }

            var updated = record with
            {
                PaperOutcomes = outcomes,
                MarketAssessmentOutcomes = assessmentOutcomes,
                BiasSummary = DecisionBiasSummary.From(record.MarketAssessments, record.CandidateOpportunities),
            };
            await _writer.SaveAsync(file, updated, cancellationToken);
            evaluatedRecords.Add(updated);
        }

        var reportPath = BuildReportPath(request.RootPath, request.TradingDate);
        var report = CreateReport(request, evaluatedAtUtc, evaluatedRecords, ToArtifactReference(reportPath));
        await SaveReportAsync(reportPath, report, cancellationToken);
        return report;
    }

    private async Task<PaperTradeOutcome> EvaluateCandidateAsync(
        DecisionAuditRecord record,
        DecisionAuditCandidate candidate,
        PriceResolution resolution,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken)
    {
        if (candidate.SetupExpiresAtUtc <= record.ReviewedAtUtc)
        {
            return new PaperTradeOutcome(
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
                0,
                "Setup expiry was not after the review timestamp.");
        }

        var result = await _marketDataService.GetBarsAsync(
            new MarketDataRequest(
                candidate.Instrument,
                resolution,
                record.ReviewedAtUtc,
                candidate.SetupExpiresAtUtc.Add(GetResolutionInterval(resolution)),
                AllowBackfill: false),
            cancellationToken);

        if (result.Series.Bars.Count == 0)
        {
            return new PaperTradeOutcome(
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
                0,
                FormatMarketDataIssue(result, "No local market-data bars were available for the setup outcome window."));
        }

        if (result.Status != MarketDataStatus.Completed)
        {
            return new PaperTradeOutcome(
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
                result.Series.Bars.Count,
                FormatMarketDataIssue(result, "Local market data was incomplete for the setup outcome window."));
        }

        return _outcomeEvaluator.Evaluate(candidate, record.ReviewedAtUtc, result.Series, evaluatedAtUtc);
    }

    private async Task<PaperMarketAssessmentOutcome> EvaluateAssessmentAsync(
        DecisionAuditRecord record,
        DecisionAuditAssessment assessment,
        PriceResolution resolution,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken)
    {
        var horizonEndsAtUtc = record.ReviewedAtUtc.Add(AssessmentOutcomeHorizon);
        var result = await _marketDataService.GetBarsAsync(
            new MarketDataRequest(
                assessment.Instrument,
                resolution,
                record.ReviewedAtUtc,
                horizonEndsAtUtc.Add(GetResolutionInterval(resolution)),
                AllowBackfill: false),
            cancellationToken);

        if (result.Series.Bars.Count == 0)
        {
            return new PaperMarketAssessmentOutcome(
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
                0,
                FormatMarketDataIssue(result, "No local market-data bars were available for the assessment horizon."));
        }

        if (result.Status != MarketDataStatus.Completed)
        {
            return new PaperMarketAssessmentOutcome(
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
                result.Series.Bars.Count,
                FormatMarketDataIssue(result, "Local market data was incomplete for the assessment horizon."));
        }

        return _assessmentEvaluator.Evaluate(
            assessment,
            record.ReviewedAtUtc,
            horizonEndsAtUtc,
            result.Series,
            evaluatedAtUtc);
    }

    private static DecisionAuditEvaluationReport CreateReport(
        DecisionAuditEvaluationRequest request,
        DateTimeOffset evaluatedAtUtc,
        IReadOnlyList<DecisionAuditRecord> records,
        ArtifactReference reportArtifact)
    {
        var allOutcomes = records.SelectMany(record => record.PaperOutcomes).ToArray();
        var allAssessmentOutcomes = records.SelectMany(record => record.MarketAssessmentOutcomes).ToArray();
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
            reportArtifact);
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

    private static string FormatMarketDataIssue(MarketDataResult result, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            return result.Message;
        }

        if (result.Gaps.Count == 0)
        {
            return fallback;
        }

        var firstGap = result.Gaps[0];
        return $"{fallback} First missing range: {firstGap.FromUtc:O} to {firstGap.ToUtc:O}.";
    }

    private static ArtifactReference ToArtifactReference(string path)
        => new(Path.GetFullPath(path), new Uri(Path.GetFullPath(path)).AbsoluteUri);
}
