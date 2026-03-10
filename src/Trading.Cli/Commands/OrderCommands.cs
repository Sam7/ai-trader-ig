using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Trading.Abstractions;

[Description("List recent order activity.")]
public sealed class ListOrdersCommand : AsyncCommand<ListOrdersSettings>
{
    private readonly ITradingGateway _gateway;
    private readonly TradingCliRenderer _renderer;

    public ListOrdersCommand(ITradingGateway gateway, TradingCliRenderer renderer)
    {
        _gateway = gateway;
        _renderer = renderer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ListOrdersSettings settings, CancellationToken cancellationToken)
    {
        var toUtc = settings.To ?? DateTimeOffset.UtcNow;
        var fromUtc = settings.From ?? toUtc.AddHours(-24);

        await _gateway.AuthenticateAsync(cancellationToken);
        var orders = await _gateway.GetOrdersAsync(new OrderQuery(fromUtc, toUtc, settings.Max), cancellationToken);
        _renderer.WriteOrders(orders);
        return 0;
    }
}

[Description("Show status for a deal reference.")]
public sealed class ShowOrderStatusCommand : AsyncCommand<ShowOrderStatusSettings>
{
    private readonly ITradingGateway _gateway;
    private readonly TradingCliRenderer _renderer;

    public ShowOrderStatusCommand(ITradingGateway gateway, TradingCliRenderer renderer)
    {
        _gateway = gateway;
        _renderer = renderer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ShowOrderStatusSettings settings, CancellationToken cancellationToken)
    {
        await _gateway.AuthenticateAsync(cancellationToken);
        var status = await _gateway.GetOrderStatusAsync(settings.DealReference, cancellationToken);
        _renderer.WriteOrderStatus(status);
        return 0;
    }
}

public sealed class ListOrdersSettings : CommandSettings
{
    [CommandOption("--from <ISO-8601>")]
    public DateTimeOffset? From { get; init; }

    [CommandOption("--to <ISO-8601>")]
    public DateTimeOffset? To { get; init; }

    [CommandOption("--max <COUNT>")]
    public int Max { get; init; } = 100;

    public override ValidationResult Validate()
    {
        if (Max <= 0)
        {
            return ValidationResult.Error("Option --max must be greater than zero.");
        }

        if (From is not null && To is not null && To < From)
        {
            return ValidationResult.Error("Option --to must be greater than or equal to --from.");
        }

        return ValidationResult.Success();
    }
}

public sealed class ShowOrderStatusSettings : CommandSettings
{
    [CommandOption("--deal-reference <REFERENCE>")]
    public string DealReference { get; init; } = string.Empty;

    public override ValidationResult Validate()
    {
        return CliParsing.Require(!string.IsNullOrWhiteSpace(DealReference), "Missing required option --deal-reference.");
    }
}
