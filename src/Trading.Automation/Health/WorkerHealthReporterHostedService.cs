using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.Abstractions;
using Trading.Automation.Configuration;
using Trading.MarketData;

namespace Trading.Automation.Health;

public sealed class WorkerHealthReporterHostedService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private readonly WorkerHealthOptions _options;
    private readonly MarketDataOptions _marketDataOptions;
    private readonly MarketDataCollectorOptions _collectorOptions;
    private readonly DailyBriefingOptions _dailyBriefingOptions;
    private readonly IHostEnvironment _environment;
    private readonly IMarketDataStore _marketDataStore;
    private readonly IMarketDataHealthStore _healthStore;
    private readonly IMarketDataRecoveryStore _recoveryStore;
    private readonly IMarketDataObjectStore _objectStore;
    private readonly MarketDataStreamPipelineMetrics _streamMetrics;
    private readonly WorkerOperationMetrics _operationMetrics;
    private readonly SlackAlertService _slackAlertService;
    private readonly ILogger<WorkerHealthReporterHostedService> _logger;
    private int _criticalSamples;
    private bool _degradedAlertSent;

    public WorkerHealthReporterHostedService(
        IOptions<WorkerHealthOptions> options,
        IOptions<MarketDataOptions> marketDataOptions,
        IOptions<MarketDataCollectorOptions> collectorOptions,
        IOptions<DailyBriefingOptions> dailyBriefingOptions,
        IHostEnvironment environment,
        IMarketDataStore marketDataStore,
        IMarketDataHealthStore healthStore,
        IMarketDataRecoveryStore recoveryStore,
        IMarketDataObjectStore objectStore,
        MarketDataStreamPipelineMetrics streamMetrics,
        WorkerOperationMetrics operationMetrics,
        SlackAlertService slackAlertService,
        ILogger<WorkerHealthReporterHostedService> logger)
    {
        _options = options.Value;
        _marketDataOptions = marketDataOptions.Value;
        _collectorOptions = collectorOptions.Value;
        _dailyBriefingOptions = dailyBriefingOptions.Value;
        _environment = environment;
        _marketDataStore = marketDataStore;
        _healthStore = healthStore;
        _recoveryStore = recoveryStore;
        _objectStore = objectStore;
        _streamMetrics = streamMetrics;
        _operationMetrics = operationMetrics;
        _slackAlertService = slackAlertService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        _options.Validate();
        await ReportOnceAsync(stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(_options.Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await ReportOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ReportOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await BuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
            await WriteLocalAsync(snapshot, cancellationToken).ConfigureAwait(false);
            await UploadAsync(snapshot, cancellationToken).ConfigureAwait(false);
            await AlertAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Worker health reporting failed.");
        }
    }

    private async Task<WorkerHealthSnapshot> BuildSnapshotAsync(CancellationToken cancellationToken)
    {
        using var process = Process.GetCurrentProcess();
        var gcInfo = GC.GetGCMemoryInfo();
        var stream = _streamMetrics.Snapshot();
        var marketData = await BuildMarketDataSummaryAsync(stream, cancellationToken).ConfigureAwait(false);
        var processHealth = new ProcessHealthSnapshot(
            process.Id,
            DateTimeOffset.UtcNow - process.StartTime.ToUniversalTime(),
            process.WorkingSet64,
            process.PrivateMemorySize64,
            process.Threads.Count,
            SafeHandleCount(process));
        var gc = new GcHealthSnapshot(
            GC.GetTotalMemory(forceFullCollection: false),
            gcInfo.HeapSizeBytes,
            gcInfo.FragmentedBytes,
            gcInfo.TotalCommittedBytes,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));
        var reasons = new List<string>();
        var status = ResolveStatus(processHealth, stream, marketData, reasons);
        return new WorkerHealthSnapshot(
            DateTimeOffset.UtcNow,
            Environment.MachineName,
            _environment.EnvironmentName,
            status,
            reasons,
            processHealth,
            gc,
            stream,
            marketData)
        {
            Operations = _operationMetrics.Snapshot(),
        };
    }

    private async Task<MarketDataHealthSummary> BuildMarketDataSummaryAsync(
        MarketDataStreamPipelineSnapshot stream,
        CancellationToken cancellationToken)
    {
        var instruments = new List<MarketDataInstrumentHealth>();
        foreach (var configured in ResolveTrackedInstruments())
        {
            var latest = await _marketDataStore.GetLatestFinalAsync(
                configured,
                _collectorOptions.Resolution,
                cancellationToken).ConfigureAwait(false);
            var health = await _healthStore.GetAsync(configured, _collectorOptions.Resolution, cancellationToken)
                .ConfigureAwait(false);
            instruments.Add(new MarketDataInstrumentHealth(
                configured.Value,
                latest?.Bar.TimestampUtc,
                health?.LastReceivedUpdateUtc,
                health?.ConnectionState.ToString(),
                health?.RepairState.ToString()));
        }

        var recoveryWork = await _recoveryStore.GetRecoveryWorkItemsAsync(cancellationToken).ConfigureAwait(false);
        var recoveryBudget = await _recoveryStore.GetHistoricalAllowanceBudgetAsync(cancellationToken).ConfigureAwait(false);
        return new MarketDataHealthSummary(
            instruments
                .Select(instrument => instrument.LatestFinalBarUtc)
                .Where(value => value is not null)
                .DefaultIfEmpty()
                .Max(),
            stream.LastReceivedUpdateUtc,
            stream.LastPersistedUpdateUtc,
            instruments,
            BuildRecoveryHealth(recoveryWork, recoveryBudget, DateTimeOffset.UtcNow));
    }

    private WorkerHealthStatus ResolveStatus(
        ProcessHealthSnapshot process,
        MarketDataStreamPipelineSnapshot stream,
        MarketDataHealthSummary marketData,
        List<string> reasons)
    {
        var status = WorkerHealthStatus.Healthy;
        var memory = WorkerMemoryPolicy.Assess(process.WorkingSetBytes, _options, _criticalSamples);
        if (memory.Status != WorkerHealthStatus.Healthy)
        {
            status = Max(status, memory.Status);
            reasons.Add(memory.Reason!);
        }

        var ingestion = _marketDataOptions.StreamIngestion;
        if (stream.DispatcherDepth >= ingestion.DispatcherCapacity * ingestion.CriticalQueueUtilization
            || stream.IngestorDepth >= ingestion.DispatcherCapacity * ingestion.CriticalQueueUtilization)
        {
            status = WorkerHealthStatus.Critical;
            reasons.Add("Market-data stream queue depth is critical.");
        }
        else if (stream.DispatcherDepth >= ingestion.DispatcherCapacity * ingestion.WarningQueueUtilization
            || stream.IngestorDepth >= ingestion.DispatcherCapacity * ingestion.WarningQueueUtilization)
        {
            status = Max(status, WorkerHealthStatus.Warning);
            reasons.Add("Market-data stream queue depth is elevated.");
        }

        if (stream.RejectedFinalUpdates > 0)
        {
            status = WorkerHealthStatus.Critical;
            reasons.Add("One or more final market-data candles were rejected by the stream dispatcher.");
        }

        if (marketData.LatestFinalBarUtc is null)
        {
            status = Max(status, WorkerHealthStatus.Warning);
            reasons.Add("No final market-data bar is available for configured instruments.");
        }

        if (marketData.Recovery?.AllowanceBlockedRanges > 0)
        {
            status = Max(status, WorkerHealthStatus.Warning);
            reasons.Add(FormatAllowanceReason(marketData.Recovery));
        }

        if (marketData.Recovery?.PermanentlyBlockedRanges > 0)
        {
            status = Max(status, WorkerHealthStatus.Warning);
            reasons.Add($"{marketData.Recovery.PermanentlyBlockedRanges} automatic market-data recovery range(s) are permanently blocked.");
        }

        return status;
    }

    private async Task WriteLocalAsync(WorkerHealthSnapshot snapshot, CancellationToken cancellationToken)
    {
        var directory = Path.GetFullPath(_options.LocalDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "worker-status.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task UploadAsync(WorkerHealthSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_marketDataOptions.CloudSnapshot.BucketName)
            || string.IsNullOrWhiteSpace(_options.GcsObjectName))
        {
            return;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"ai-trader-health-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(snapshot, JsonOptions), cancellationToken)
                .ConfigureAwait(false);
            await _objectStore.UploadAsync(
                _marketDataOptions.CloudSnapshot.BucketName,
                _options.GcsObjectName,
                tempPath,
                new Dictionary<string, string>
                {
                    ["status"] = snapshot.Status.ToString(),
                    ["observed-at-utc"] = snapshot.ObservedAtUtc.ToString("O"),
                },
                "application/json",
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private async Task AlertAsync(WorkerHealthSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.Status == WorkerHealthStatus.Healthy)
        {
            _criticalSamples = 0;
            if (_degradedAlertSent)
            {
                var recovered = await _slackAlertService.SendStateChangeAsync(
                    "worker-health",
                    "healthy",
                    WorkerAlertSeverity.Warning,
                    "AI Trader worker health recovered",
                    "Worker health returned to healthy.",
                    cancellationToken).ConfigureAwait(false);
                if (recovered)
                {
                    _degradedAlertSent = false;
                }
            }

            return;
        }

        var severity = snapshot.Status == WorkerHealthStatus.Critical
            ? WorkerAlertSeverity.Critical
            : WorkerAlertSeverity.Warning;
        var delivered = await _slackAlertService.SendStateChangeAsync(
            "worker-health",
            WorkerHealthAlertFingerprint.Create(snapshot.Status, snapshot.Reasons),
            severity,
            "AI Trader worker health degraded",
            string.Join('\n', snapshot.Reasons),
            cancellationToken).ConfigureAwait(false);
        _degradedAlertSent |= delivered;

        var memory = WorkerMemoryPolicy.Assess(
            snapshot.Process.WorkingSetBytes,
            _options,
            _criticalSamples);
        _criticalSamples = memory.ConsecutiveCriticalSamples;

        if (memory.ShouldFailFast)
        {
            await _slackAlertService.SendAsync(
                "worker-health-failfast",
                WorkerAlertSeverity.Critical,
                "AI Trader worker exiting before host OOM",
                $"Working set stayed above fail-fast threshold for {_criticalSamples} samples. Exiting for systemd restart.",
                cancellationToken).ConfigureAwait(false);
            Environment.Exit(137);
        }
    }

    private IReadOnlyList<InstrumentId> ResolveTrackedInstruments()
        => _dailyBriefingOptions.TrackedMarkets
            .Select(market => market.InstrumentId)
            .Where(instrument => !string.IsNullOrWhiteSpace(instrument))
            .Select(instrument => new InstrumentId(instrument))
            .Distinct()
            .ToArray();

    private static WorkerHealthStatus Max(WorkerHealthStatus left, WorkerHealthStatus right)
        => left >= right ? left : right;

    private static int SafeHandleCount(Process process)
    {
        try
        {
            return process.HandleCount;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    internal static string FormatAllowanceReason(MarketDataRecoveryHealth recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);

        if (recovery.AllowanceExpiresAtUtc is not { } expiry)
        {
            return "Historical recovery is blocked by IG allowance; IG did not provide a reset time.";
        }

        var estimate = recovery.AllowanceExpiryEstimated ? "approximately " : string.Empty;
        var suffix = recovery.AllowanceExpiryEstimated
            ? " (estimated; IG did not provide a reset time)."
            : ".";
        return $"Historical recovery is blocked by IG allowance until {estimate}{expiry:O}{suffix}";
    }

    internal static MarketDataRecoveryHealth BuildRecoveryHealth(
        IReadOnlyList<MarketDataRecoveryState> recovery,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(recovery);

        var pending = recovery.Where(x => !x.IsComplete).ToArray();
        var blocked = pending
            .Where(x => x.AllowanceExpiresAtUtc is { } expiry && expiry > now)
            .OrderBy(x => x.AllowanceExpiresAtUtc)
            .ThenBy(x => x.Instrument.Value, StringComparer.Ordinal)
            .ThenBy(x => x.FromUtc)
            .ToArray();
        var active = blocked.FirstOrDefault() ?? pending.FirstOrDefault();

        return new MarketDataRecoveryHealth(
            pending.Length,
            blocked.Length,
            active?.RemainingAllowance,
            active?.AllowanceExpiresAtUtc,
            active?.Instrument.Value)
        {
            AllowanceExpiryEstimated = active is not null
                && active.LastFailure?.Contains("allowance", StringComparison.OrdinalIgnoreCase) == true,
        };
    }

    internal static MarketDataRecoveryHealth BuildRecoveryHealth(
        IReadOnlyList<MarketDataRecoveryWorkItem> recovery,
        HistoricalAllowanceBudget? budget,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(recovery);

        var pending = recovery
            .Where(item => item.Status == MarketDataRecoveryWorkStatus.Pending)
            .ToArray();
        var recentPending = pending
            .Where(item => item.Reason == MarketDataRecoveryReason.RecentTail)
            .ToArray();
        var historicalPending = pending
            .Where(item => item.Reason == MarketDataRecoveryReason.HistoricalAudit)
            .ToArray();
        var allowanceBlocked = budget?.RemainingPoints is <= 0
            && budget.ResetAtUtc is { } resetAtUtc
            && resetAtUtc > now
            ? historicalPending
            : [];
        var permanentlyBlocked = recovery
            .Where(item => item.Status == MarketDataRecoveryWorkStatus.Blocked)
            .OrderBy(item => item.Instrument.Value, StringComparer.Ordinal)
            .ThenBy(item => item.FromUtc)
            .ToArray();
        var active = allowanceBlocked
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.ToUtc)
            .FirstOrDefault()
            ?? permanentlyBlocked.FirstOrDefault()
            ?? recentPending.OrderBy(item => item.Priority).ThenByDescending(item => item.ToUtc).FirstOrDefault()
            ?? historicalPending.OrderBy(item => item.Priority).ThenByDescending(item => item.ToUtc).FirstOrDefault();

        return new MarketDataRecoveryHealth(
            pending.Length,
            allowanceBlocked.Length + permanentlyBlocked.Length,
            budget?.RemainingPoints,
            budget?.ResetAtUtc,
            active?.Instrument.Value)
        {
            AllowanceExpiryEstimated = budget?.ResetEstimated == true,
            RecentPendingRanges = recentPending.Length,
            HistoricalPendingRanges = historicalPending.Length,
            AllowanceBlockedRanges = allowanceBlocked.Length,
            PermanentlyBlockedRanges = permanentlyBlocked.Length,
            NextAttemptUtc = active?.NextAttemptUtc,
            LastFailure = active?.LastFailure,
        };
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
