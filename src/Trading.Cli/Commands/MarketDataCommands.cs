using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Trading.Abstractions;
using Trading.MarketData;

[Description("Run the market-data stream collector until cancelled or for a bounded duration.")]
public sealed class CollectMarketDataCommand : AsyncCommand<CollectMarketDataSettings>
{
    private readonly IMarketDataCollector _collector;
    private readonly IAnsiConsole _console;

    public CollectMarketDataCommand(IMarketDataCollector collector, IAnsiConsole console)
    {
        _collector = collector;
        _console = console;
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        CollectMarketDataSettings settings,
        CancellationToken cancellationToken)
    {
        var instruments = ParseInstruments(settings.Instruments);
        var duration = CollectMarketDataSettings.ParseDuration(settings.Duration);

        await _collector.RunAsync(instruments, duration, cancellationToken);

        _console.MarkupLine($"Market-data collector completed for [cyan]{instruments.Count}[/] instrument(s).");
        return 0;
    }

    private static IReadOnlyList<InstrumentId> ParseInstruments(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(instrument => new InstrumentId(instrument))
            .ToArray();
}

public sealed class CollectMarketDataSettings : CommandSettings
{
    private static readonly TimeSpan MaxBoundedDuration = TimeSpan.FromDays(7);

    [CommandOption("--instruments <EPICS>")]
    public string Instruments { get; init; } = string.Empty;

    [CommandOption("--duration <TIMESPAN>")]
    [Description("Optional duration. Omit to run until cancelled. HH:mm:ss values support hours greater than 23.")]
    public string? Duration { get; init; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Instruments))
        {
            return ValidationResult.Error("Missing required option --instruments.");
        }

        var instruments = Instruments.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (instruments.Length == 0)
        {
            return ValidationResult.Error("Option --instruments must include at least one EPIC.");
        }

        if (instruments.Any(instrument => instrument.Any(char.IsWhiteSpace)))
        {
            return ValidationResult.Error("Option --instruments must contain comma-separated EPICs without whitespace.");
        }

        if (!TryParseDuration(Duration, out var parsedDuration))
        {
            return ValidationResult.Error("Option --duration must be a valid TimeSpan.");
        }

        if (parsedDuration is null)
        {
            return ValidationResult.Success();
        }

        if (parsedDuration < TimeSpan.Zero)
        {
            return ValidationResult.Error("Option --duration must be zero or greater.");
        }

        if (parsedDuration > MaxBoundedDuration)
        {
            return ValidationResult.Error("Option --duration must be 7 days or less. Omit --duration to run indefinitely.");
        }

        return ValidationResult.Success();
    }

    public static TimeSpan? ParseDuration(string? value)
        => TryParseDuration(value, out var duration)
            ? duration
            : throw new FormatException("Option --duration must be a valid TimeSpan.");

    public static bool TryParseDuration(string? value, out TimeSpan? duration)
    {
        duration = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (value.Contains('.', StringComparison.Ordinal))
        {
            if (TimeSpan.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                duration = parsed;
                return true;
            }

            return false;
        }

        var parts = value.Split(':');
        if (parts.Length == 3
            && long.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var hours)
            && int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var minutes)
            && int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            && hours >= 0
            && minutes is >= 0 and <= 59
            && seconds is >= 0 and <= 59)
        {
            try
            {
                duration = TimeSpan.FromHours(hours)
                    .Add(TimeSpan.FromMinutes(minutes))
                    .Add(TimeSpan.FromSeconds(seconds));
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        if (TimeSpan.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var fallback))
        {
            duration = fallback;
            return true;
        }

        return false;
    }
}

[Description("Download and import the latest configured cloud market-data snapshot once.")]
public sealed class SyncMarketDataMirrorCommand : AsyncCommand
{
    private readonly MarketDataSnapshotSynchronizer _synchronizer;
    private readonly IAnsiConsole _console;

    public SyncMarketDataMirrorCommand(MarketDataSnapshotSynchronizer synchronizer, IAnsiConsole console)
    {
        _synchronizer = synchronizer;
        _console = console;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var result = await _synchronizer.SynchronizeOnceAsync(cancellationToken);
        _console.MarkupLine($"Market-data mirror sync: [cyan]{result.Status}[/] - {Markup.Escape(result.Message)}");
        if (result.LatestBarUtc is not null)
        {
            _console.MarkupLine($"Latest mirrored bar: [cyan]{result.LatestBarUtc:O}[/]");
        }

        if (!string.IsNullOrWhiteSpace(result.LocalSnapshotPath))
        {
            _console.MarkupLine($"Snapshot: [grey]{Markup.Escape(result.LocalSnapshotPath)}[/]");
        }

        return result.Status == MarketDataSnapshotRefreshStatus.Failed
            ? 2
            : 0;
    }
}

[Description("Show local cloud market-data mirror status.")]
public sealed class ShowMarketDataMirrorStatusCommand : AsyncCommand
{
    private readonly MarketDataMirrorStatusService _statusService;
    private readonly IAnsiConsole _console;

    public ShowMarketDataMirrorStatusCommand(MarketDataMirrorStatusService statusService, IAnsiConsole console)
    {
        _statusService = statusService;
        _console = console;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var status = await _statusService.GetStatusAsync(cancellationToken);
        var table = new Table().Title("Market Data Mirror");
        table.AddColumn("Field");
        table.AddColumn("Value");
        table.AddRow("Enabled", status.Enabled.ToString());
        table.AddRow("Configured", status.IsConfigured.ToString());
        table.AddRow("Stale", status.IsStale.ToString());
        table.AddRow("Remote Checked", status.RemoteObjectChecked.ToString());
        table.AddRow("Remote Object Stale", status.IsRemoteObjectStale.ToString());
        table.AddRow("Remote Latest Bar Stale", status.IsRemoteLatestBarStale.ToString());
        table.AddRow("Last Attempt UTC", status.LastAttemptUtc?.ToString("O") ?? "-");
        table.AddRow("Last Success UTC", status.LastSuccessfulSyncUtc?.ToString("O") ?? "-");
        table.AddRow("Latest Bar UTC", status.LatestBarUtc?.ToString("O") ?? "-");
        table.AddRow("Remote Updated UTC", status.RemoteUpdatedUtc?.ToString("O") ?? "-");
        table.AddRow("Remote Latest Bar UTC", status.RemoteLatestBarUtc?.ToString("O") ?? "-");
        table.AddRow("Remote Generation", status.RemoteGeneration ?? "-");
        table.AddRow("Remote SHA-256", status.RemoteSha256 ?? "-");
        table.AddRow("Snapshot", status.LocalSnapshotPath ?? "-");
        table.AddRow("Last Status", status.LastStatus.ToString());
        table.AddRow("Message", status.LastMessage ?? "-");
        table.AddRow("Diagnosis", status.Diagnosis);
        _console.Write(table);
        return status.Enabled && (!status.IsConfigured || status.IsStale || status.IsRemoteObjectStale || status.IsRemoteLatestBarStale)
            ? 2
            : 0;
    }
}

[Description("Explicitly backfill historical market data from IG REST.")]
public sealed class BackfillMarketDataCommand : AsyncCommand<BackfillMarketDataSettings>
{
    private readonly MarketDataHistoricalBackfillService _backfill;
    private readonly IAnsiConsole _console;

    public BackfillMarketDataCommand(MarketDataHistoricalBackfillService backfill, IAnsiConsole console)
    {
        _backfill = backfill;
        _console = console;
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        BackfillMarketDataSettings settings,
        CancellationToken cancellationToken)
    {
        var count = await _backfill.BackfillAsync(
            new InstrumentId(settings.Instrument),
            CliParsing.ParsePriceResolution(settings.Resolution),
            settings.From!.Value,
            settings.To!.Value,
            cancellationToken);

        _console.MarkupLine($"Backfilled [cyan]{count}[/] historical bar(s) from IG REST.");
        return 0;
    }
}

public sealed class BackfillMarketDataSettings : CommandSettings
{
    [CommandOption("-i|--instrument <EPIC>")]
    public string Instrument { get; init; } = string.Empty;

    [CommandOption("--resolution <VALUE>")]
    public string Resolution { get; init; } = string.Empty;

    [CommandOption("--from <ISO-8601>")]
    public DateTimeOffset? From { get; init; }

    [CommandOption("--to <ISO-8601>")]
    public DateTimeOffset? To { get; init; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Instrument))
        {
            return ValidationResult.Error("Missing required option --instrument.");
        }

        if (Instrument.Any(char.IsWhiteSpace))
        {
            return ValidationResult.Error("Option --instrument must be a single EPIC without whitespace.");
        }

        if (!CliParsing.IsValidPriceResolution(Resolution))
        {
            return ValidationResult.Error("Option --resolution is required and must be supported.");
        }

        if (From is null || To is null)
        {
            return ValidationResult.Error("Options --from and --to are required.");
        }

        if (From >= To)
        {
            return ValidationResult.Error("Option --from must be earlier than --to.");
        }

        return ValidationResult.Success();
    }
}
