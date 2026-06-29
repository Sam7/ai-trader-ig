using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Trading.Abstractions;
using Trading.MarketData;

[Description("Run the market-data stream collector for a bounded duration.")]
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
        var duration = TimeSpan.Parse(settings.Duration, System.Globalization.CultureInfo.InvariantCulture);

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
    [CommandOption("--instruments <EPICS>")]
    public string Instruments { get; init; } = string.Empty;

    [CommandOption("--duration <TIMESPAN>")]
    public string Duration { get; init; } = "00:10:00";

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

        if (!TimeSpan.TryParse(Duration, System.Globalization.CultureInfo.InvariantCulture, out var parsedDuration))
        {
            return ValidationResult.Error("Option --duration must be a valid TimeSpan.");
        }

        if (parsedDuration < TimeSpan.Zero)
        {
            return ValidationResult.Error("Option --duration must be zero or greater.");
        }

        return ValidationResult.Success();
    }
}
