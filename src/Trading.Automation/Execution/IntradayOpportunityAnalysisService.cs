using Trading.AI.DailyBriefing;
using Trading.AI.PromptExecution;
using Trading.AI.Prompts;
using Trading.Automation.Health;
using System.Text;

namespace Trading.Automation.Execution;

public sealed class IntradayOpportunityAnalysisService : IIntradayOpportunityAnalysisService
{
    private readonly IIntradayOpportunityReviewer _reviewer;
    private readonly WorkerOperationMetrics _operationMetrics;

    public IntradayOpportunityAnalysisService(
        IIntradayOpportunityReviewer reviewer,
        WorkerOperationMetrics operationMetrics)
    {
        _reviewer = reviewer;
        _operationMetrics = operationMetrics;
    }

    public PromptContractProvenance Contract => _reviewer.Contract;

    public string RenderRequestText(IntradayOpportunityReviewRequest request)
        => _reviewer.RenderRequestText(request);

    public async Task<IntradayOpportunityReviewExecution> AnalyzeAsync(
        IntradayOpportunityPreparationDocument prepared,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                prepared.SchemaVersion,
                IntradayOpportunityPreparationDocument.CurrentSchemaVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Prepared artifact '{prepared.PreparedArtifact.Path}' uses unsupported preparation schema version '{prepared.SchemaVersion}'.");
        }

        var contract = Contract;
        if (!string.Equals(prepared.PromptId, contract.PromptId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Prepared prompt ID '{prepared.PromptId}' does not match prompt contract '{contract.PromptId}'.");
        }

        if (prepared.PromptContract != contract)
        {
            throw new InvalidOperationException(
                $"Prepared prompt contract for '{prepared.PreparedArtifact.Path}' no longer matches the current prompt or response schema. Regenerate the prepared run before submitting.");
        }

        var renderedRequestText = RenderRequestText(prepared.Request);
        if (!string.Equals(renderedRequestText, prepared.RenderedRequestText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Prepared request text for '{prepared.PreparedArtifact.Path}' no longer matches the current prompt template. Regenerate the prepared run before submitting.");
        }

        var requestSha256 = IntradayOpportunityPreparationWriter.ComputeSha256(
            Encoding.UTF8.GetBytes(prepared.RenderedRequestText));
        if (!string.Equals(requestSha256, prepared.RequestSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Prepared request text for '{prepared.PreparedArtifact.Path}' failed its SHA-256 integrity check.");
        }

        var evidenceById = new Dictionary<string, (DecisionEvidence Evidence, byte[] Data)>(StringComparer.Ordinal);
        foreach (var evidence in prepared.Evidence)
        {
            if (evidenceById.ContainsKey(evidence.EvidenceId))
            {
                throw new InvalidOperationException(
                    $"Prepared evidence ID '{evidence.EvidenceId}' is duplicated in '{prepared.PreparedArtifact.Path}'.");
            }

            var loadStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var data = await File.ReadAllBytesAsync(evidence.Artifact.Path, cancellationToken);
            loadStopwatch.Stop();
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            _operationMetrics.Record(
                "intraday-evidence-load",
                1,
                data.Length,
                loadStopwatch.Elapsed,
                process.WorkingSet64);
            if (!string.Equals(
                    IntradayOpportunityPreparationWriter.ComputeSha256(data),
                    evidence.Sha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Prepared evidence '{evidence.EvidenceId}' failed its SHA-256 integrity check.");
            }

            evidenceById.Add(evidence.EvidenceId, (evidence, data));
        }

        foreach (var market in prepared.Markets)
        {
            foreach (var evidenceId in market.EvidenceIds)
            {
                if (!evidenceById.TryGetValue(evidenceId, out var item))
                {
                    throw new InvalidOperationException(
                        $"Prepared market '{market.InstrumentId}' references missing evidence '{evidenceId}'.");
                }

                if (!string.Equals(
                        item.Evidence.Instrument?.Value,
                        market.InstrumentId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Prepared evidence '{evidenceId}' does not belong to prepared market '{market.InstrumentId}'.");
                }
            }
        }

        var attachments = new List<PromptAttachment>(prepared.Attachments.Count);
        foreach (var attachment in prepared.Attachments)
        {
            if (!evidenceById.TryGetValue(attachment.EvidenceId, out var item))
            {
                throw new InvalidOperationException(
                    $"Prepared attachment '{attachment.EvidenceId}' does not reference evidence in '{prepared.PreparedArtifact.Path}'.");
            }

            if (!string.Equals(item.Evidence.Label, attachment.Label, StringComparison.Ordinal)
                || !string.Equals(item.Evidence.MediaType, attachment.MediaType, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Prepared attachment '{attachment.EvidenceId}' does not match its evidence metadata.");
            }

            attachments.Add(new PromptAttachment(
                attachment.Label,
                attachment.MediaType,
                item.Data));
        }

        return await _reviewer.ReviewAsync(prepared.Request, attachments, cancellationToken);
    }
}
