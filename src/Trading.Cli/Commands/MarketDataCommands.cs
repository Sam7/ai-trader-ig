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

    private static bool TryParseDuration(string? value, out TimeSpan? duration)
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
