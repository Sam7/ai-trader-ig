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
                case "working-create":
                    await CreateWorkingOrderAsync(options);
                    return 0;
                case "working-update":
                    await UpdateWorkingOrderAsync(options);
                    return 0;
                case "working-cancel":
                    await CancelWorkingOrderAsync(options);
                    return 0;
                case "working-orders":
                    await ShowWorkingOrdersAsync();
                    return 0;
                case "close":
                    await ClosePositionAsync(options);
                    return 0;
                case "position-update":
                    await UpdatePositionAsync(options);
                    return 0;
                case "positions":
                    await ShowPositionsAsync();
                    return 0;
                case "markets-search":
                    await SearchMarketsAsync(options);
                    return 0;
                case "markets-browse":
                    await BrowseMarketsAsync(options);
                    return 0;
                case "prices":
                    await ShowPricesAsync(options);
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

    private async Task UpdatePositionAsync(CliOptions options)
    {
        var dealId = options.Required("deal-id");
        var stopLevel = options.TryGetDecimal("stop-level", out var stop) ? stop : (decimal?)null;
        var limitLevel = options.TryGetDecimal("limit-level", out var limit) ? limit : (decimal?)null;
        var trailingDistance = options.TryGetDecimal("trailing-stop-distance", out var trailingStopDistance) ? trailingStopDistance : (decimal?)null;
        var trailingIncrement = options.TryGetDecimal("trailing-stop-increment", out var trailingStopIncrement) ? trailingStopIncrement : (decimal?)null;

        await _gateway.AuthenticateAsync();
        var result = await _gateway.UpdatePositionAsync(new UpdatePositionRequest(
            dealId,
            stopLevel,
            limitLevel,
            trailingDistance,
            trailingIncrement));
        Console.WriteLine($"Position updated. Ref={result.DealReference}, DealId={result.DealId}, Status={result.Status}, Message={result.Message ?? "n/a"}");
    }

    private async Task CreateWorkingOrderAsync(CliOptions options)
    {
        var instrument = options.Required("instrument");
        var direction = ParseDirection(options.Required("direction"));
        var type = ParseWorkingOrderType(options.Required("type"));
        var size = options.RequiredDecimal("size");
        var level = options.RequiredDecimal("level");
        var tif = ParseTimeInForce(options.TryGet("time-in-force", out var tifRaw) ? tifRaw! : "gtc");
        var goodTillDate = options.TryGetDateTimeOffset("good-till-date", out var gtdValue)
            ? gtdValue
            : (DateTimeOffset?)null;

        await _gateway.AuthenticateAsync();
        var result = await _gateway.PlaceWorkingOrderAsync(new CreateWorkingOrderRequest(
            new InstrumentId(instrument),
            direction,
            type,
            size,
            level,
            tif,
            goodTillDate));
        Console.WriteLine($"Working order created. Ref={result.DealReference}, DealId={result.DealId ?? "n/a"}, Status={result.Status}, Message={result.Message ?? "n/a"}");
    }

    private async Task UpdateWorkingOrderAsync(CliOptions options)
    {
        var dealId = options.Required("deal-id");
        var level = options.TryGet("level", out var levelRaw)
            ? decimal.Parse(levelRaw!, System.Globalization.CultureInfo.InvariantCulture)
            : (decimal?)null;
        var type = options.TryGet("type", out var typeRaw) ? ParseWorkingOrderType(typeRaw!) : (WorkingOrderType?)null;
        var tif = options.TryGet("time-in-force", out var tifRaw) ? ParseTimeInForce(tifRaw!) : (WorkingOrderTimeInForce?)null;
        var goodTillDate = options.TryGetDateTimeOffset("good-till-date", out var gtdValue)
            ? gtdValue
            : (DateTimeOffset?)null;

        await _gateway.AuthenticateAsync();
        var result = await _gateway.UpdateWorkingOrderAsync(new UpdateWorkingOrderRequest(dealId, level, type, tif, goodTillDate));
        Console.WriteLine($"Working order updated. Ref={result.DealReference}, DealId={result.DealId ?? "n/a"}, Status={result.Status}, Message={result.Message ?? "n/a"}");
    }

    private async Task CancelWorkingOrderAsync(CliOptions options)
    {
        var dealId = options.Required("deal-id");

        await _gateway.AuthenticateAsync();
        var result = await _gateway.CancelWorkingOrderAsync(dealId);
        Console.WriteLine($"Working order cancelled. Ref={result.DealReference}, DealId={result.DealId ?? "n/a"}, Status={result.Status}, Message={result.Message ?? "n/a"}");
    }

    private async Task ShowWorkingOrdersAsync()
    {
        await _gateway.AuthenticateAsync();
        var workingOrders = await _gateway.GetWorkingOrdersAsync();

        if (workingOrders.Count == 0)
        {
            Console.WriteLine("No working orders.");
            return;
        }

        foreach (var order in workingOrders)
        {
            Console.WriteLine($"DealId={order.DealId} Instrument={order.Instrument} Direction={order.Direction} Type={order.Type} Size={order.Size} Level={order.Level} Tif={order.TimeInForce} Status={order.Status} Created={order.CreatedAtUtc:O}");
        }
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
            Console.WriteLine($"DealId={position.DealId} Instrument={position.Instrument} Direction={position.Direction} Size={position.Size} Currency={position.Currency} Stop={position.StopLevel?.ToString() ?? "n/a"} Limit={position.LimitLevel?.ToString() ?? "n/a"} Created={position.CreatedAtUtc:O}");
        }
    }

    private async Task SearchMarketsAsync(CliOptions options)
    {
        var query = options.Required("query");
        var max = options.TryGetInt("max", out var maxValue) ? maxValue : 20;

        await _gateway.AuthenticateAsync();
        var markets = await _gateway.SearchMarketsAsync(query, max);

        if (markets.Count == 0)
        {
            Console.WriteLine("No matching markets.");
            return;
        }

        foreach (var market in markets)
        {
            Console.WriteLine($"Instrument={market.Instrument} Name={market.Name} Type={market.Type ?? "n/a"} Expiry={market.Expiry ?? "n/a"} Currency={market.CurrencyCode ?? "n/a"} Status={market.Status}");
        }
    }

    private async Task BrowseMarketsAsync(CliOptions options)
    {
        var nodeId = options.TryGet("node-id", out var nodeValue) ? nodeValue : null;

        await _gateway.AuthenticateAsync();
        var page = await _gateway.BrowseMarketsAsync(nodeId);

        Console.WriteLine($"Node={page.Name} Id={page.CurrentNodeId ?? "root"}");

        foreach (var node in page.Nodes)
        {
            Console.WriteLine($"ChildNode={node.Id} Name={node.Name}");
        }

        foreach (var market in page.Markets)
        {
            Console.WriteLine($"Market={market.Instrument} Name={market.Name} Status={market.Status}");
        }
    }

    private async Task ShowPricesAsync(CliOptions options)
    {
        var instrument = options.Required("instrument");
        var resolution = options.TryGet("resolution", out var resolutionValue)
            ? ParsePriceResolution(resolutionValue!)
            : (PriceResolution?)null;
        var max = options.TryGetInt("max", out var maxValue) ? maxValue : (int?)null;
        var from = options.TryGetDateTimeOffset("from", out var fromValue) ? fromValue : (DateTimeOffset?)null;
        var to = options.TryGetDateTimeOffset("to", out var toValue) ? toValue : (DateTimeOffset?)null;

        await _gateway.AuthenticateAsync();
        var series = await _gateway.GetPricesAsync(new GetPricesRequest(new InstrumentId(instrument), resolution, max, from, to));

        if (series.Bars.Count == 0)
        {
            Console.WriteLine("No prices returned.");
            return;
        }

        foreach (var bar in series.Bars)
        {
            Console.WriteLine($"{bar.TimestampUtc:O} BidO={bar.BidOpen} BidH={bar.BidHigh} BidL={bar.BidLow} BidC={bar.BidClose} AskO={bar.AskOpen} AskH={bar.AskHigh} AskL={bar.AskLow} AskC={bar.AskClose} Vol={bar.Volume?.ToString() ?? "n/a"}");
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
        Console.WriteLine("  working-create --instrument <epic> --direction <buy|sell> --type <limit|stop> --size <decimal> --level <decimal> [--time-in-force <gtc|gtd>] [--good-till-date <ISO-8601>]");
        Console.WriteLine("  working-update --deal-id <id> [--level <decimal>] [--type <limit|stop>] [--time-in-force <gtc|gtd>] [--good-till-date <ISO-8601>]");
        Console.WriteLine("  working-cancel --deal-id <id>");
        Console.WriteLine("  working-orders");
        Console.WriteLine("  close --deal-id <id> [--size <decimal>]");
        Console.WriteLine("  position-update --deal-id <id> [--stop-level <decimal>] [--limit-level <decimal>] [--trailing-stop-distance <decimal>] [--trailing-stop-increment <decimal>]");
        Console.WriteLine("  positions");
        Console.WriteLine("  markets-search --query <text> [--max <int>]");
        Console.WriteLine("  markets-browse [--node-id <id>]");
        Console.WriteLine("  prices --instrument <epic> [--resolution <value>] [--max <int>] [--from <ISO-8601>] [--to <ISO-8601>]");
        Console.WriteLine("  orders [--from <ISO-8601>] [--to <ISO-8601>] [--max <int>]");
        Console.WriteLine("  status --deal-reference <value>");
    }

    private static TradeDirection ParseDirection(string value)
        => value.Equals("sell", StringComparison.OrdinalIgnoreCase)
            ? TradeDirection.Sell
            : TradeDirection.Buy;

    private static WorkingOrderType ParseWorkingOrderType(string value)
        => value.Equals("stop", StringComparison.OrdinalIgnoreCase)
            ? WorkingOrderType.Stop
            : WorkingOrderType.Limit;

    private static WorkingOrderTimeInForce ParseTimeInForce(string value)
        => value.Equals("gtd", StringComparison.OrdinalIgnoreCase)
            || value.Equals("good_till_date", StringComparison.OrdinalIgnoreCase)
            || value.Equals("good-till-date", StringComparison.OrdinalIgnoreCase)
                ? WorkingOrderTimeInForce.GoodTillDate
                : WorkingOrderTimeInForce.GoodTillCancelled;

    private static PriceResolution ParsePriceResolution(string value)
        => value.ToLowerInvariant() switch
        {
            "second" => PriceResolution.Second,
            "minute" => PriceResolution.Minute,
            "2minute" or "minute_2" => PriceResolution.TwoMinutes,
            "3minute" or "minute_3" => PriceResolution.ThreeMinutes,
            "5minute" or "minute_5" => PriceResolution.FiveMinutes,
            "10minute" or "minute_10" => PriceResolution.TenMinutes,
            "15minute" or "minute_15" => PriceResolution.FifteenMinutes,
            "30minute" or "minute_30" => PriceResolution.ThirtyMinutes,
            "hour" => PriceResolution.Hour,
            "2hour" or "hour_2" => PriceResolution.TwoHours,
            "3hour" or "hour_3" => PriceResolution.ThreeHours,
            "4hour" or "hour_4" => PriceResolution.FourHours,
            "day" => PriceResolution.Day,
            "week" => PriceResolution.Week,
            "month" => PriceResolution.Month,
            _ => throw new ArgumentException($"Unsupported resolution '{value}'."),
        };
}
