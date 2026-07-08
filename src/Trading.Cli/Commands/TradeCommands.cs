using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Trading.Abstractions;
using Trading.Execution;

[Description("Place a market buy order.")]
public sealed class BuyTradeCommand : PlaceTradeCommand
{
    public BuyTradeCommand(
        ITradingGateway gateway,
        IExecutionSubmissionService executionSubmissionService,
        TradingCliRenderer renderer)
        : base(gateway, executionSubmissionService, renderer, TradeDirection.Buy)
    {
    }
}

[Description("Place a market sell order.")]
public sealed class SellTradeCommand : PlaceTradeCommand
{
    public SellTradeCommand(
        ITradingGateway gateway,
        IExecutionSubmissionService executionSubmissionService,
        TradingCliRenderer renderer)
        : base(gateway, executionSubmissionService, renderer, TradeDirection.Sell)
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

    [CommandOption("--operation-id <ID>")]
    [Description("Stable idempotency key for this manual submission.")]
    public string? OperationId { get; init; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Instrument))
        {
            return ValidationResult.Error("Missing required option --instrument.");
        }

        if (OperationId is not null && string.IsNullOrWhiteSpace(OperationId))
        {
            return ValidationResult.Error("Option --operation-id cannot be blank.");
        }

        return CliParsing.Require(Size > 0, "Option --size must be greater than zero.");
    }
}

public abstract class PlaceTradeCommand : AsyncCommand<TradeSettings>
{
    private readonly ITradingGateway _gateway;
    private readonly IExecutionSubmissionService _executionSubmissionService;
    private readonly TradingCliRenderer _renderer;
    private readonly TradeDirection _direction;

    protected PlaceTradeCommand(
        ITradingGateway gateway,
        IExecutionSubmissionService executionSubmissionService,
        TradingCliRenderer renderer,
        TradeDirection direction)
    {
        _gateway = gateway;
        _executionSubmissionService = executionSubmissionService;
        _renderer = renderer;
        _direction = direction;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, TradeSettings settings, CancellationToken cancellationToken)
    {
        await _gateway.AuthenticateAsync(cancellationToken);
        var result = await _executionSubmissionService.SubmitMarketOrderAsync(
            CliExecutionOperationIds.Resolve(settings.OperationId),
            ExecutionOperationSource.ManualCli,
            new PlaceOrderRequest(new InstrumentId(settings.Instrument), _direction, settings.Size),
            cancellationToken);

        _renderer.WriteExecutionSubmission($"{_direction} Submitted", result);

        return 0;
    }
}
