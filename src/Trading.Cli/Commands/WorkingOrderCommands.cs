using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Trading.Abstractions;

[Description("List open working orders.")]
public sealed class ListWorkingOrdersCommand : AsyncCommand<EmptyCommandSettings>
{
    private readonly ITradingGateway _gateway;
    private readonly TradingCliRenderer _renderer;

    public ListWorkingOrdersCommand(ITradingGateway gateway, TradingCliRenderer renderer)
    {
        _gateway = gateway;
        _renderer = renderer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, EmptyCommandSettings settings, CancellationToken cancellationToken)
    {
        await _gateway.AuthenticateAsync(cancellationToken);
        var workingOrders = await _gateway.GetWorkingOrdersAsync(cancellationToken);
        _renderer.WriteWorkingOrders(workingOrders);
        return 0;
    }
}

[Description("Create a working order.")]
public sealed class CreateWorkingOrderCommand : AsyncCommand<CreateWorkingOrderSettings>
{
    private readonly ITradingGateway _gateway;
    private readonly TradingCliRenderer _renderer;

    public CreateWorkingOrderCommand(ITradingGateway gateway, TradingCliRenderer renderer)
    {
        _gateway = gateway;
        _renderer = renderer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, CreateWorkingOrderSettings settings, CancellationToken cancellationToken)
    {
        await _gateway.AuthenticateAsync(cancellationToken);
        var result = await _gateway.PlaceWorkingOrderAsync(
            new CreateWorkingOrderRequest(
                new InstrumentId(settings.Instrument),
                CliParsing.ParseDirection(settings.Direction),
                CliParsing.ParseWorkingOrderType(settings.Type),
                settings.Size,
                settings.Level,
                CliParsing.ParseTimeInForce(settings.TimeInForce ?? "gtc"),
                settings.GoodTillDate),
            cancellationToken);

        _renderer.WriteSubmission(
            "Working Order Created",
            result.DealReference,
            result.DealId,
            result.Status,
            result.Message,
            result.TimestampUtc);

        return 0;
    }
}

[Description("Update a working order.")]
public sealed class UpdateWorkingOrderCommand : AsyncCommand<UpdateWorkingOrderSettings>
{
    private readonly ITradingGateway _gateway;
    private readonly TradingCliRenderer _renderer;

    public UpdateWorkingOrderCommand(ITradingGateway gateway, TradingCliRenderer renderer)
    {
        _gateway = gateway;
        _renderer = renderer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, UpdateWorkingOrderSettings settings, CancellationToken cancellationToken)
    {
        await _gateway.AuthenticateAsync(cancellationToken);
        var result = await _gateway.UpdateWorkingOrderAsync(
            new UpdateWorkingOrderRequest(
                settings.DealId,
                settings.Level,
                settings.Type is null ? null : CliParsing.ParseWorkingOrderType(settings.Type),
                settings.TimeInForce is null ? null : CliParsing.ParseTimeInForce(settings.TimeInForce),
                settings.GoodTillDate),
            cancellationToken);

        _renderer.WriteSubmission(
            "Working Order Updated",
            result.DealReference,
            result.DealId,
            result.Status,
            result.Message,
            result.TimestampUtc);

        return 0;
    }
}

[Description("Cancel a working order.")]
public sealed class CancelWorkingOrderCommand : AsyncCommand<CancelWorkingOrderSettings>
{
    private readonly ITradingGateway _gateway;
    private readonly TradingCliRenderer _renderer;

    public CancelWorkingOrderCommand(ITradingGateway gateway, TradingCliRenderer renderer)
    {
        _gateway = gateway;
        _renderer = renderer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, CancelWorkingOrderSettings settings, CancellationToken cancellationToken)
    {
        await _gateway.AuthenticateAsync(cancellationToken);
        var result = await _gateway.CancelWorkingOrderAsync(settings.DealId, cancellationToken);
        _renderer.WriteSubmission(
            "Working Order Cancelled",
            result.DealReference,
            result.DealId,
            result.Status,
            result.Message,
            result.TimestampUtc);

        return 0;
    }
}

public sealed class CreateWorkingOrderSettings : CommandSettings
{
    [CommandOption("-i|--instrument <EPIC>")]
    public string Instrument { get; init; } = string.Empty;

    [CommandOption("-d|--direction <DIRECTION>")]
    public string Direction { get; init; } = string.Empty;

    [CommandOption("-t|--type <TYPE>")]
    public string Type { get; init; } = string.Empty;

    [CommandOption("-s|--size <SIZE>")]
    public decimal Size { get; init; }

    [CommandOption("-l|--level <LEVEL>")]
    public decimal Level { get; init; }

    [CommandOption("--time-in-force <TIF>")]
    public string? TimeInForce { get; init; }

    [CommandOption("--good-till-date <ISO-8601>")]
    public DateTimeOffset? GoodTillDate { get; init; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Instrument))
        {
            return ValidationResult.Error("Missing required option --instrument.");
        }

        if (!CliParsing.IsValidDirection(Direction))
        {
            return ValidationResult.Error("Option --direction must be buy or sell.");
        }

        if (!CliParsing.IsValidWorkingOrderType(Type))
        {
            return ValidationResult.Error("Option --type must be limit or stop.");
        }

        if (Size <= 0)
        {
            return ValidationResult.Error("Option --size must be greater than zero.");
        }

        if (Level <= 0)
        {
            return ValidationResult.Error("Option --level must be greater than zero.");
        }

        return TimeInForce is null || CliParsing.IsValidTimeInForce(TimeInForce)
            ? ValidationResult.Success()
            : ValidationResult.Error("Option --time-in-force must be gtc or gtd.");
    }
}

public sealed class UpdateWorkingOrderSettings : CommandSettings
{
    [CommandOption("--deal-id <ID>")]
    public string DealId { get; init; } = string.Empty;

    [CommandOption("--level <LEVEL>")]
    public decimal? Level { get; init; }

    [CommandOption("--type <TYPE>")]
    public string? Type { get; init; }

    [CommandOption("--time-in-force <TIF>")]
    public string? TimeInForce { get; init; }

    [CommandOption("--good-till-date <ISO-8601>")]
    public DateTimeOffset? GoodTillDate { get; init; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(DealId))
        {
            return ValidationResult.Error("Missing required option --deal-id.");
        }

        if (Level is <= 0)
        {
            return ValidationResult.Error("Option --level must be greater than zero when provided.");
        }

        if (Type is not null && !CliParsing.IsValidWorkingOrderType(Type))
        {
            return ValidationResult.Error("Option --type must be limit or stop.");
        }

        if (TimeInForce is not null && !CliParsing.IsValidTimeInForce(TimeInForce))
        {
            return ValidationResult.Error("Option --time-in-force must be gtc or gtd.");
        }

        return ValidationResult.Success();
    }
}

public sealed class CancelWorkingOrderSettings : CommandSettings
{
    [CommandOption("--deal-id <ID>")]
    public string DealId { get; init; } = string.Empty;

    public override ValidationResult Validate()
    {
        return CliParsing.Require(!string.IsNullOrWhiteSpace(DealId), "Missing required option --deal-id.");
    }
}
