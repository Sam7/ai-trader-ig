using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.Automation.Configuration;
using Trading.MarketData;

namespace Trading.Automation.Diagnostics;

internal interface IWorkerDiagnosticsArtifactUploader
{
    Task<IReadOnlyList<string>> UploadAsync(
        IReadOnlyList<string> artifactPaths,
        CancellationToken cancellationToken = default);
}

internal sealed class NoOpWorkerDiagnosticsArtifactUploader : IWorkerDiagnosticsArtifactUploader
{
    public Task<IReadOnlyList<string>> UploadAsync(
        IReadOnlyList<string> artifactPaths,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>([]);
}

/// <summary>Uploads only closed, bounded diagnostics artifacts after a later worker start.</summary>
internal sealed class GcsWorkerDiagnosticsArtifactUploader : IWorkerDiagnosticsArtifactUploader
{
    private readonly WorkerDiagnosticsOptions _diagnostics;
    private readonly MarketDataOptions _marketData;
    private readonly IMarketDataObjectStore _objectStore;
    private readonly ILogger<GcsWorkerDiagnosticsArtifactUploader> _logger;

    public GcsWorkerDiagnosticsArtifactUploader(
        IOptions<WorkerDiagnosticsOptions> diagnostics,
        IOptions<MarketDataOptions> marketData,
        IMarketDataObjectStore objectStore,
        ILogger<GcsWorkerDiagnosticsArtifactUploader> logger)
    {
        _diagnostics = diagnostics.Value;
        _marketData = marketData.Value;
        _objectStore = objectStore;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> UploadAsync(
        IReadOnlyList<string> artifactPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactPaths);

        if (!_diagnostics.UploadClosedSegments
            || string.IsNullOrWhiteSpace(_marketData.CloudSnapshot.BucketName))
        {
            return [];
        }

        var uploaded = new List<string>();
        foreach (var artifactPath in artifactPaths)
        {
            if (!File.Exists(artifactPath))
            {
                continue;
            }

            try
            {
                var artifact = new FileInfo(artifactPath);
                var kind = artifact.Name.StartsWith("exit-", StringComparison.Ordinal)
                    ? "exit-evidence"
                    : "trace";
                await _objectStore.UploadAsync(
                    _marketData.CloudSnapshot.BucketName,
                    BuildObjectName(artifact),
                    artifact.FullName,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["diagnostic-kind"] = kind,
                        ["created-at-utc"] = artifact.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture),
                        ["size-bytes"] = artifact.Length.ToString(CultureInfo.InvariantCulture),
                    },
                    kind == "trace" ? "application/x-ndjson" : "application/json",
                    cancellationToken).ConfigureAwait(false);
                uploaded.Add(artifact.FullName);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Unable to upload closed worker diagnostics artifact {ArtifactName}; retaining it for a later start.",
                    Path.GetFileName(artifactPath));
            }
        }

        return uploaded;
    }

    private string BuildObjectName(FileInfo artifact)
    {
        var prefix = _diagnostics.GcsPrefix.Trim('/');
        var worker = Sanitize(Environment.MachineName);
        var day = artifact.LastWriteTimeUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $"{prefix}/{worker}/{day}/{artifact.Name}";
    }

    private static string Sanitize(string value)
        => string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
}
