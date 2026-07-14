using System.Security.Cryptography;
using System.Text.Json;
using Trading.Abstractions;

namespace Trading.Automation.Execution;

public sealed class DecisionEvidenceSidecarWriter
{
    private readonly DecisionAuditWriter _auditWriter;

    public DecisionEvidenceSidecarWriter(DecisionAuditWriter auditWriter)
    {
        _auditWriter = auditWriter;
    }

    public async Task<DecisionEvaluationWriteResult> WriteEvaluationAsync(
        string sourceAuditPath,
        DecisionAuditRecord sourceAudit,
        DateTimeOffset evaluatedAtUtc,
        PriceResolution resolution,
        AuditDataQualityPolicy dataQualityPolicy,
        IReadOnlyList<PaperTradeOutcome> paperOutcomes,
        IReadOnlyList<PaperMarketAssessmentOutcome> marketAssessmentOutcomes,
        DecisionAuditDataQualitySummary dataQuality,
        CancellationToken cancellationToken = default)
    {
        var sourceArtifact = ToArtifactReference(sourceAuditPath);
        var sourceSha256 = await ComputeFileSha256Async(sourceArtifact.Path, cancellationToken);
        var recordId = CreateRecordId(sourceAudit.AuditId, "evaluation", evaluatedAtUtc);
        var record = new DecisionEvaluationRecord(
            "1",
            recordId,
            sourceAudit.AuditId,
            sourceArtifact,
            sourceSha256,
            evaluatedAtUtc,
            resolution,
            dataQualityPolicy,
            paperOutcomes,
            marketAssessmentOutcomes,
            DecisionBiasSummary.From(sourceAudit.MarketAssessments, sourceAudit.CandidateOpportunities),
            dataQuality,
            "1");
        var path = BuildSidecarPath(sourceArtifact.Path, "decision-evaluation", evaluatedAtUtc);
        await WriteNewAsync(path, record, cancellationToken);
        return new DecisionEvaluationWriteResult(record, ToArtifactReference(path));
    }

    public async Task<ArtifactReference> WriteDemoExecutionAsync(
        string sourceAuditPath,
        DemoCanaryExecutionSnapshot execution,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var sourceAudit = await _auditWriter.LoadAsync(sourceAuditPath, cancellationToken);
        var sourceArtifact = ToArtifactReference(sourceAuditPath);
        var sourceSha256 = await ComputeFileSha256Async(sourceArtifact.Path, cancellationToken);
        var record = new DemoCanaryExecutionRecord(
            "1",
            CreateRecordId(sourceAudit.AuditId, "demo-execution", recordedAtUtc),
            sourceAudit.AuditId,
            sourceArtifact,
            sourceSha256,
            recordedAtUtc,
            execution);
        var path = BuildSidecarPath(sourceArtifact.Path, "demo-execution", recordedAtUtc);
        await WriteNewAsync(path, record, cancellationToken);
        return ToArtifactReference(path);
    }

    private static async Task WriteNewAsync<T>(
        string path,
        T record,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, record, DecisionAuditJson.Options, cancellationToken);
    }

    private static string BuildSidecarPath(
        string sourceAuditPath,
        string sidecarKind,
        DateTimeOffset occurredAtUtc)
    {
        var directory = Path.GetDirectoryName(sourceAuditPath)!;
        var stem = Path.GetFileNameWithoutExtension(sourceAuditPath);
        var sourceStem = stem.EndsWith("-decision-audit", StringComparison.Ordinal)
            ? stem[..^"-decision-audit".Length]
            : stem;
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(
            directory,
            $"{sourceStem}-{sidecarKind}-{occurredAtUtc.ToUniversalTime():yyyyMMddTHHmmssfffZ}-{uniqueSuffix}.json");
    }

    private static string CreateRecordId(
        string sourceAuditId,
        string kind,
        DateTimeOffset occurredAtUtc)
        => $"{sourceAuditId}/{kind}/{occurredAtUtc.ToUniversalTime():yyyyMMddTHHmmssfffZ}/{Guid.NewGuid():N}";

    private static async Task<string> ComputeFileSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static ArtifactReference ToArtifactReference(string path)
        => new(Path.GetFullPath(path), new Uri(Path.GetFullPath(path)).AbsoluteUri);
}
