using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Trading.Abstractions;

[Description("List open positions.")]
public sealed class ListPositionsCommand : AsyncCommand<EmptyCommandSettings>
{
    private readonly ITradingGateway _gateway;
    private readonly TradingCliRenderer _renderer;

    public ListPositionsCommand(ITradingGateway gateway, TradingCliRenderer renderer)
    {
        _gateway = gateway;
        _renderer = renderer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, EmptyCommandSettings settings, CancellationToken cancellationToken)
    {
        await _gateway.AuthenticateAsync(cancellationToken);
        var positions = await _gateway.GetOpenPositionsAsync(cancellationToken);
        _renderer.WritePositions(positions);
        return 0;
    }
}

[Description("Close an open position.")]
public sealed class ClosePositionCommand : AsyncCommand<ClosePositionSettings>
{
    private readonly ITradingGateway _gateway;
    private readonly TradingCliRenderer _renderer;

    public ClosePositionCommand(ITradingGateway gateway, TradingCliRenderer renderer)
    {
        _gateway = gateway;
        _renderer = renderer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ClosePositionSettings settings, CancellationToken cancellationToken)
    {
        await _gateway.AuthenticateAsync(cancellationToken);
        var result = await _gateway.ClosePositionAsync(new ClosePositionRequest(settings.DealId, settings.Size), cancellationToken);
        _renderer.WriteSubmission(
            "Position Closed",
            result.DealReference,
            result.DealId,
            result.Status,
            result.Message,
            result.TimestampUtc);

        return 0;
    }
}

[Description("Update an open position.")]
public sealed class UpdatePositionCommand : AsyncCommand<UpdatePositionSettings>
{
    private readonly ITradingGateway _gateway;
    private readonly TradingCliRenderer _renderer;

    public UpdatePositionCommand(ITradingGateway gateway, TradingCliRenderer renderer)
    {
        _gateway = gateway;
        _renderer = renderer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, UpdatePositionSettings settings, CancellationToken cancellationToken)
    {
        await _gateway.AuthenticateAsync(cancellationToken);
        var result = await _gateway.UpdatePositionAsync(
            new UpdatePositionRequest(
                settings.DealId,
                settings.StopLevel,
                settings.LimitLevel,
                settings.TrailingStopDistance,
                settings.TrailingStopIncrement),
            cancellationToken);

        _renderer.WriteSubmission(
            "Position Updated",
            result.DealReference,
            result.DealId,
            result.Status,
            result.Message,
            result.TimestampUtc);

        return 0;
    }
}

public sealed class ClosePositionSettings : CommandSettings
{
    [CommandOption("--deal-id <ID>")]
    public string DealId { get; init; } = string.Empty;

    [CommandOption("-s|--size <SIZE>")]
    public decimal? Size { get; init; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(DealId))
        {
            return ValidationResult.Error("Missing required option --deal-id.");
        }

        return Size is null || Size > 0
            ? ValidationResult.Success()
            : ValidationResult.Error("Option --size must be greater than zero when provided.");
    }
}

public sealed class UpdatePositionSettings : CommandSettings
{
    [CommandOption("--deal-id <ID>")]
    public string DealId { get; init; } = string.Empty;

    [CommandOption("--stop-level <LEVEL>")]
    public decimal? StopLevel { get; init; }

    [CommandOption("--limit-level <LEVEL>")]
    public decimal? LimitLevel { get; init; }

    [CommandOption("--trailing-stop-distance <DISTANCE>")]
    public decimal? TrailingStopDistance { get; init; }

    [CommandOption("--trailing-stop-increment <INCREMENT>")]
    public decimal? TrailingStopIncrement { get; init; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(DealId))
        {
            return ValidationResult.Error("Missing required option --deal-id.");
        }

        if (StopLevel is null
            && LimitLevel is null
            && TrailingStopDistance is null
            && TrailingStopIncrement is null)
        {
            return ValidationResult.Error("At least one amendment must be provided.");
        }

        if (TrailingStopIncrement is not null && TrailingStopDistance is null)
        {
            return ValidationResult.Error("Option --trailing-stop-increment requires --trailing-stop-distance.");
        }

        return ValidationResult.Success();
    }
}
