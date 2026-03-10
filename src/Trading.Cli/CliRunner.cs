using Trading.Abstractions;

public sealed class CliRunner
{
    private readonly ITradingGateway _gateway;

    public CliRunner(ITradingGateway gateway)
    {
        _gateway = gateway;
    }

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        var options = CliOptions.Parse(args.Skip(1));

        try
        {
            switch (command)
            {
                case "auth":
                    await AuthenticateAsync();
                    return 0;
                case "buy":
                    await PlaceOrderAsync(TradeDirection.Buy, options);
                    return 0;
                case "sell":
                    await PlaceOrderAsync(TradeDirection.Sell, options);
                    return 0;
                case "close":
                    await ClosePositionAsync(options);
                    return 0;
                case "positions":
                    await ShowPositionsAsync();
                    return 0;
                case "orders":
                    await ShowOrdersAsync(options);
                    return 0;
                case "status":
                    await ShowStatusAsync(options);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown command: {command}");
                    PrintHelp();
                    return 1;
            }
        }
        catch (TradingGatewayException exception)
        {
            Console.Error.WriteLine($"Trading error ({exception.ErrorCode}): {exception.Message}");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Unexpected error: {exception.Message}");
            return 99;
        }
    }

    private async Task AuthenticateAsync()
    {
        var session = await _gateway.AuthenticateAsync();
        Console.WriteLine($"Authenticated to {session.BrokerName} account {session.AccountId} at {session.AuthenticatedAtUtc:O}");
    }

    private async Task PlaceOrderAsync(TradeDirection direction, CliOptions options)
    {
        var instrument = options.Required("instrument");
        var size = options.RequiredDecimal("size");

        await _gateway.AuthenticateAsync();
        var result = await _gateway.PlaceMarketOrderAsync(new PlaceOrderRequest(new InstrumentId(instrument), direction, size));
        Console.WriteLine($"{direction} submitted. Ref={result.DealReference}, DealId={result.DealId ?? "n/a"}, Status={result.Status}, Message={result.Message ?? "n/a"}");
    }

    private async Task ClosePositionAsync(CliOptions options)
    {
        var dealId = options.Required("deal-id");
        var hasSize = options.TryGet("size", out var sizeRaw);
        decimal? size = hasSize
            ? decimal.Parse(sizeRaw!, System.Globalization.CultureInfo.InvariantCulture)
            : null;

        await _gateway.AuthenticateAsync();
        var result = await _gateway.ClosePositionAsync(new ClosePositionRequest(dealId, size));
        Console.WriteLine($"Close submitted. Ref={result.DealReference}, DealId={result.DealId ?? "n/a"}, Status={result.Status}, Message={result.Message ?? "n/a"}");
    }

    private async Task ShowPositionsAsync()
    {
        await _gateway.AuthenticateAsync();
        var positions = await _gateway.GetOpenPositionsAsync();

        if (positions.Count == 0)
        {
            Console.WriteLine("No open positions.");
            return;
        }

        foreach (var position in positions)
        {
            Console.WriteLine($"DealId={position.DealId} Instrument={position.Instrument} Direction={position.Direction} Size={position.Size} Currency={position.Currency} Created={position.CreatedAtUtc:O}");
        }
    }

    private async Task ShowOrdersAsync(CliOptions options)
    {
        var toUtc = options.TryGetDateTimeOffset("to", out var toValue)
            ? toValue
            : DateTimeOffset.UtcNow;

        var fromUtc = options.TryGetDateTimeOffset("from", out var fromValue)
            ? fromValue
            : toUtc.AddHours(-24);

        var maxItems = options.TryGetInt("max", out var maxValue) ? maxValue : 100;

        await _gateway.AuthenticateAsync();
        var orders = await _gateway.GetOrdersAsync(new OrderQuery(fromUtc, toUtc, maxItems));

        if (orders.Count == 0)
        {
            Console.WriteLine("No orders in range.");
            return;
        }

        foreach (var order in orders)
        {
            Console.WriteLine($"Ref={order.DealReference} DealId={order.DealId ?? "n/a"} Instrument={order.Instrument?.ToString() ?? "n/a"} Direction={order.Direction?.ToString() ?? "n/a"} Size={order.Size?.ToString() ?? "n/a"} Status={order.Status} Time={order.TimestampUtc:O}");
        }
    }

    private async Task ShowStatusAsync(CliOptions options)
    {
        var dealReference = options.Required("deal-reference");
        await _gateway.AuthenticateAsync();
        var status = await _gateway.GetOrderStatusAsync(dealReference);

        if (status is null)
        {
            Console.WriteLine("Order not found.");
            return;
        }

        Console.WriteLine($"Ref={status.DealReference} DealId={status.DealId ?? "n/a"} Status={status.Status} Message={status.Message ?? "n/a"} Time={status.TimestampUtc:O}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  auth");
        Console.WriteLine("  buy --instrument <epic> --size <decimal>");
        Console.WriteLine("  sell --instrument <epic> --size <decimal>");
        Console.WriteLine("  close --deal-id <id> [--size <decimal>]");
        Console.WriteLine("  positions");
        Console.WriteLine("  orders [--from <ISO-8601>] [--to <ISO-8601>] [--max <int>]");
        Console.WriteLine("  status --deal-reference <value>");
    }
}
