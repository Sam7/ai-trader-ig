using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Trading.Abstractions;

[Description("Place a market buy order.")]
public sealed class BuyTradeCommand : PlaceTradeCommand
{
    public BuyTradeCommand(ITradingGateway gateway, TradingCliRenderer renderer)
        : base(gateway, renderer, TradeDirection.Buy)
    {
    }
}

[Description("Place a market sell order.")]
public sealed class SellTradeCommand : PlaceTradeCommand
{
    public SellTradeCommand(ITradingGateway gateway, TradingCliRenderer renderer)
        : base(gateway, renderer, TradeDirection.Sell)
    {
    }
}

public sealed class TradeSettings : CommandSettings
{
    [CommandOption("-i|--instrument <EPIC>")]
    [Description("IG instrument epic.")]
    public string Instrument { get; init; } = string.Empty;

    [CommandOption("-s|--size <SIZE>")]
    [Description("Order size.")]
    public decimal Size { get; init; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Instrument))
        {
            return ValidationResult.Error("Missing required option --instrument.");
        }

        return CliParsing.Require(Size > 0, "Option --size must be greater than zero.");
    }
}

public abstract class PlaceTradeCommand : AsyncCommand<TradeSettings>
{
    private readonly ITradingGateway _gateway;
    private readonly TradingCliRenderer _renderer;
    private readonly TradeDirection _direction;

    protected PlaceTradeCommand(ITradingGateway gateway, TradingCliRenderer renderer, TradeDirection direction)
    {
        _gateway = gateway;
        _renderer = renderer;
        _direction = direction;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, TradeSettings settings, CancellationToken cancellationToken)
    {
        await _gateway.AuthenticateAsync(cancellationToken);
        var result = await _gateway.PlaceMarketOrderAsync(
            new PlaceOrderRequest(new InstrumentId(settings.Instrument), _direction, settings.Size),
            cancellationToken);

        _renderer.WriteSubmission(
            $"{_direction} Submitted",
            result.DealReference,
            result.DealId,
            result.Status,
            result.Message,
            result.TimestampUtc);

        return 0;
    }
}
