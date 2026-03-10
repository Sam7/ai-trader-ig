using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Trading.Abstractions;

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
