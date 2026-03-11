using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Trading.Abstractions;
using Trading.Charting;

[Description("Search markets by text query.")]
public sealed class SearchMarketsCommand : AsyncCommand<SearchMarketsSettings>
{
    private readonly ITradingGateway _gateway;
    private readonly TradingCliRenderer _renderer;

    public SearchMarketsCommand(ITradingGateway gateway, TradingCliRenderer renderer)
    {
        _gateway = gateway;
        _renderer = renderer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, SearchMarketsSettings settings, CancellationToken cancellationToken)
    {
        await _gateway.AuthenticateAsync(cancellationToken);
        var markets = await _gateway.SearchMarketsAsync(settings.Query, settings.Max, cancellationToken);
        _renderer.WriteMarkets(markets);
        return 0;
    }
}

[Description("Browse market navigation nodes.")]
public sealed class BrowseMarketsCommand : AsyncCommand<BrowseMarketsSettings>
{
    private readonly ITradingGateway _gateway;
    private readonly TradingCliRenderer _renderer;

    public BrowseMarketsCommand(ITradingGateway gateway, TradingCliRenderer renderer)
    {
        _gateway = gateway;
        _renderer = renderer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, BrowseMarketsSettings settings, CancellationToken cancellationToken)
    {
        await _gateway.AuthenticateAsync(cancellationToken);
        var page = await _gateway.BrowseMarketsAsync(settings.NodeId, cancellationToken);
        _renderer.WriteMarketBrowsePage(page);
        return 0;
    }
}

[Description("Show historical prices for a market.")]
public sealed class ShowPricesCommand : AsyncCommand<ShowPricesSettings>
{
    private readonly ITradingGateway _gateway;
    private readonly TradingCliRenderer _renderer;

    public ShowPricesCommand(ITradingGateway gateway, TradingCliRenderer renderer)
    {
        _gateway = gateway;
        _renderer = renderer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ShowPricesSettings settings, CancellationToken cancellationToken)
    {
        await _gateway.AuthenticateAsync(cancellationToken);
        var series = await _gateway.GetPricesAsync(
            new GetPricesRequest(
                new InstrumentId(settings.Instrument),
                settings.Resolution is null ? null : CliParsing.ParsePriceResolution(settings.Resolution),
                settings.Max,
                settings.From,
                settings.To),
            cancellationToken);

        _renderer.WritePrices(series);
        return 0;
    }
}

[Description("Render a market price chart and save it as a PNG file.")]
public sealed class RenderMarketChartCommand : AsyncCommand<RenderMarketChartSettings>
{
    private readonly ITradingGateway _gateway;
    private readonly IPriceChartRenderer _chartRenderer;
    private readonly TradingCliRenderer _renderer;

    public RenderMarketChartCommand(ITradingGateway gateway, IPriceChartRenderer chartRenderer, TradingCliRenderer renderer)
    {
        _gateway = gateway;
        _chartRenderer = chartRenderer;
        _renderer = renderer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, RenderMarketChartSettings settings, CancellationToken cancellationToken)
    {
        await _gateway.AuthenticateAsync(cancellationToken);

        var series = await _gateway.GetPricesAsync(
            new GetPricesRequest(
                new InstrumentId(settings.Instrument),
                CliParsing.ParsePriceResolution(settings.Resolution),
                settings.Max,
                settings.From,
                settings.To),
            cancellationToken);

        if (series.Bars.Count == 0)
        {
            throw new CliUsageException("No prices returned for the requested range.");
        }

        byte[] imageBytes;
        try
        {
            imageBytes = _chartRenderer.RenderPng(
                series,
                CliParsing.ParsePriceChartStyle(settings.Style),
                CliParsing.ParsePriceGapMode(settings.Gaps),
                CliParsing.ParseIntegerList(settings.SimpleMovingAverageWindows),
                settings.BollingerPeriod,
                settings.Width,
                settings.Height);
        }
        catch (ArgumentException exception)
        {
            throw new CliUsageException(exception.Message);
        }

        var outputPath = Path.GetFullPath(settings.Output);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await File.WriteAllBytesAsync(outputPath, imageBytes, cancellationToken);
        _renderer.WriteChartSaved(series, outputPath);
        return 0;
    }
}

public sealed class SearchMarketsSettings : CommandSettings
{
    [CommandOption("-q|--query <TEXT>")]
    public string Query { get; init; } = string.Empty;

    [CommandOption("--max <COUNT>")]
    public int Max { get; init; } = 20;

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Query))
        {
            return ValidationResult.Error("Missing required option --query.");
        }

        return CliParsing.Require(Max > 0, "Option --max must be greater than zero.");
    }
}

public sealed class BrowseMarketsSettings : CommandSettings
{
    [CommandOption("--node-id <ID>")]
    public string? NodeId { get; init; }
}

public sealed class ShowPricesSettings : CommandSettings
{
    [CommandOption("-i|--instrument <EPIC>")]
    public string Instrument { get; init; } = string.Empty;

    [CommandOption("--resolution <VALUE>")]
    public string? Resolution { get; init; }

    [CommandOption("--max <COUNT>")]
    public int? Max { get; init; }

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

        if (Max is <= 0)
        {
            return ValidationResult.Error("Option --max must be greater than zero when provided.");
        }

        if (Resolution is not null && !CliParsing.IsValidPriceResolution(Resolution))
        {
            return ValidationResult.Error("Option --resolution is not supported.");
        }

        var hasRange = From is not null || To is not null;
        if (hasRange && (From is null || To is null))
        {
            return ValidationResult.Error("Options --from and --to must be provided together.");
        }

        if (hasRange && Max is not null)
        {
            return ValidationResult.Error("Price queries cannot specify both a range and --max.");
        }

        if ((hasRange || Max is not null) && Resolution is null)
        {
            return ValidationResult.Error("Option --resolution is required when using --max or a time range.");
        }

        if (From is not null && To is not null && From > To)
        {
            return ValidationResult.Error("Option --from must be earlier than or equal to --to.");
        }

        return ValidationResult.Success();
    }
}

public sealed class RenderMarketChartSettings : CommandSettings
{
    [CommandOption("-i|--instrument <EPIC>")]
    public string Instrument { get; init; } = string.Empty;

    [CommandOption("--resolution <VALUE>")]
    public string Resolution { get; init; } = string.Empty;

    [CommandOption("--max <COUNT>")]
    public int? Max { get; init; }

    [CommandOption("--from <ISO-8601>")]
    public DateTimeOffset? From { get; init; }

    [CommandOption("--to <ISO-8601>")]
    public DateTimeOffset? To { get; init; }

    [CommandOption("--output <PATH>")]
    public string Output { get; init; } = string.Empty;

    [CommandOption("--style <STYLE>")]
    public string Style { get; init; } = "candlestick";

    [CommandOption("--gaps <MODE>")]
    public string Gaps { get; init; } = "compress";

    [CommandOption("--sma <WINDOWS>")]
    public string? SimpleMovingAverageWindows { get; init; }

    [CommandOption("--bollinger <COUNT>")]
    public int? BollingerPeriod { get; init; }

    [CommandOption("--width <PIXELS>")]
    public int Width { get; init; } = 1200;

    [CommandOption("--height <PIXELS>")]
    public int Height { get; init; } = 800;

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Instrument))
        {
            return ValidationResult.Error("Missing required option --instrument.");
        }

        if (string.IsNullOrWhiteSpace(Output))
        {
            return ValidationResult.Error("Missing required option --output.");
        }

        if (!CliParsing.IsValidPriceResolution(Resolution))
        {
            return ValidationResult.Error("Option --resolution is required and must be supported.");
        }

        if (Max is <= 0)
        {
            return ValidationResult.Error("Option --max must be greater than zero when provided.");
        }

        if (!CliParsing.IsValidPriceChartStyle(Style))
        {
            return ValidationResult.Error("Option --style is not supported.");
        }

        if (!CliParsing.IsValidPriceGapMode(Gaps))
        {
            return ValidationResult.Error("Option --gaps is not supported.");
        }

        if (Width <= 0)
        {
            return ValidationResult.Error("Option --width must be greater than zero.");
        }

        if (Height <= 0)
        {
            return ValidationResult.Error("Option --height must be greater than zero.");
        }

        if (BollingerPeriod is <= 1)
        {
            return ValidationResult.Error("Option --bollinger must be at least 2 when provided.");
        }

        try
        {
            foreach (var window in CliParsing.ParseIntegerList(SimpleMovingAverageWindows))
            {
                if (window <= 1)
                {
                    return ValidationResult.Error("Option --sma must contain integers greater than or equal to 2.");
                }
            }
        }
        catch (FormatException)
        {
            return ValidationResult.Error("Option --sma must be a comma-separated list of integers.");
        }
        catch (OverflowException)
        {
            return ValidationResult.Error("Option --sma must be a comma-separated list of integers.");
        }

        var hasRange = From is not null || To is not null;
        if (hasRange && (From is null || To is null))
        {
            return ValidationResult.Error("Options --from and --to must be provided together.");
        }

        if (hasRange && Max is not null)
        {
            return ValidationResult.Error("Price queries cannot specify both a range and --max.");
        }

        if (!hasRange && Max is null)
        {
            return ValidationResult.Error("Option --max or both --from and --to are required.");
        }

        if (From is not null && To is not null && From > To)
        {
            return ValidationResult.Error("Option --from must be earlier than or equal to --to.");
        }

        return ValidationResult.Success();
    }
}
