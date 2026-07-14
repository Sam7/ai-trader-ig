using System.Text.Json;
using Trading.Automation.Configuration;

namespace Trading.Automation.Diagnostics;

/// <summary>Appends bounded local diagnostic traces and makes prior-run segments uploadable.</summary>
internal sealed class RollingWorkerTraceStore : IAsyncDisposable
{
    private static readonly byte[] NewLine = "\n"u8.ToArray();

    private readonly WorkerDiagnosticsOptions _options;
    private readonly string _bootId;
    private FileStream? _stream;
    private string? _activePath;
    private DateTimeOffset _lastFlushUtc;
    private int _segmentNumber;
    private bool _initialized;

    public RollingWorkerTraceStore(WorkerDiagnosticsOptions options, string bootId)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(bootId);

        options.Validate();
        _options = options;
        _bootId = Sanitize(bootId);
    }

    /// <summary>Performs filesystem work explicitly so diagnostics cannot block host construction.</summary>
    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetFullPath(_options.LocalDirectory));
        RecoverAbandonedSegments();
        _initialized = true;
    }

    public async Task AppendAsync(WorkerDiagnosticSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        EnsureStream();
        var payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, WorkerDiagnosticsJsonContext.Default.WorkerDiagnosticSnapshot);
        await _stream!.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await _stream.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);

        if (_stream.Length >= _options.SegmentMaximumBytes)
        {
            await RotateAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (_options.FlushInterval == TimeSpan.Zero
                 || DateTimeOffset.UtcNow - _lastFlushUtc >= _options.FlushInterval)
        {
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_stream is null)
        {
            return;
        }

        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        _stream.Flush(flushToDisk: true);
        _lastFlushUtc = DateTimeOffset.UtcNow;
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_stream is null || _activePath is null)
        {
            return;
        }

        await FlushAsync(cancellationToken).ConfigureAwait(false);
        await _stream.DisposeAsync().ConfigureAwait(false);
        _stream = null;
        CloseActiveSegment(_activePath);
        _activePath = null;
        PruneClosedArtifacts();
    }

    public IReadOnlyList<string> GetUploadCandidates()
    {
        var directory = Path.GetFullPath(_options.LocalDirectory);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.jsonl")
            .Concat(Directory.EnumerateFiles(directory, "exit-*.json"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    public bool TryDeleteUploadedArtifact(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetFullPath(_options.LocalDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase)
            || !IsClosedArtifactName(Path.GetFileName(fullPath)))
        {
            return false;
        }

        try
        {
            File.Delete(fullPath);
            return !File.Exists(fullPath);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
        => await CompleteAsync().ConfigureAwait(false);

    private void EnsureStream()
    {
        if (_stream is not null)
        {
            return;
        }

        Initialize();
        var directory = Path.GetFullPath(_options.LocalDirectory);
        _activePath = Path.Combine(directory, $"worker-{_bootId}-{_segmentNumber++:D4}.jsonl.active");
        _stream = new FileStream(
            _activePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        _lastFlushUtc = DateTimeOffset.UtcNow;
    }

    private async Task RotateAsync(CancellationToken cancellationToken)
    {
        await CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private void RecoverAbandonedSegments()
    {
        var directory = Path.GetFullPath(_options.LocalDirectory);
        foreach (var path in Directory.EnumerateFiles(directory, "*.jsonl.active"))
        {
            CloseActiveSegment(path);
        }

        PruneClosedArtifacts();
    }

    private void PruneClosedArtifacts()
    {
        var directory = Path.GetFullPath(_options.LocalDirectory);
        var artifacts = Directory.EnumerateFiles(directory, "*.jsonl")
            .Concat(Directory.EnumerateFiles(directory, "exit-*.json"))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();
        var closedArtifactBudget = _options.RetentionMaximumBytes - _options.SegmentMaximumBytes;
        var retainedBytes = 0L;
        foreach (var artifact in artifacts)
        {
            if (retainedBytes + artifact.Length <= closedArtifactBudget)
            {
                retainedBytes += artifact.Length;
                continue;
            }

            artifact.Delete();
        }
    }

    private static void CloseActiveSegment(string activePath)
    {
        if (!File.Exists(activePath))
        {
            return;
        }

        var finalPath = activePath[..^".active".Length];
        File.Move(activePath, finalPath, overwrite: true);
    }

    private static bool IsClosedArtifactName(string fileName)
        => fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
           || (fileName.StartsWith("exit-", StringComparison.Ordinal)
               && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

    private static string Sanitize(string value)
        => string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
}
