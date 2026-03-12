using Spectre.Console;
using Trading.AI.DailyBriefing;
using Trading.Abstractions;
using Trading.Strategy.Shared;

public sealed class TradingCliRenderer
{
    private readonly IAnsiConsole _console;

    public TradingCliRenderer(IAnsiConsole console)
    {
        _console = console;
    }

    public void WriteAuthentication(ITradingSession session)
    {
        WriteKeyValuePanel(
            "Authenticated",
            ("Broker", session.BrokerName),
            ("Account", session.AccountId),
            ("At", CliParsing.FormatDate(session.AuthenticatedAtUtc)));
    }

    public void WriteSubmission(string title, string dealReference, string? dealId, OrderStatus status, string? message, DateTimeOffset timestampUtc)
    {
        WriteKeyValuePanel(
            title,
            ("Reference", dealReference),
            ("Deal ID", dealId ?? "n/a"),
            ("Status", status.ToString()),
            ("Message", message ?? "n/a"),
            ("Time", CliParsing.FormatDate(timestampUtc)));
    }

    public void WriteWorkingOrders(IReadOnlyList<WorkingOrderSummary> workingOrders)
    {
        if (workingOrders.Count == 0)
        {
            WriteInfo("No working orders.");
            return;
        }

        var table = CreateTable("Deal ID", "Instrument", "Direction", "Type", "Size", "Level", "TIF", "Good Till", "Status", "Created");
        foreach (var order in workingOrders)
        {
            table.AddRow(
                order.DealId,
                order.Instrument.Value,
                order.Direction.ToString(),
                order.Type.ToString(),
                CliParsing.FormatDecimal(order.Size),
                CliParsing.FormatDecimal(order.Level),
                order.TimeInForce.ToString(),
                CliParsing.FormatDate(order.GoodTillDateUtc),
                order.Status.ToString(),
                CliParsing.FormatDate(order.CreatedAtUtc));
        }

        _console.Write(table);
    }

    public void WritePositions(IReadOnlyList<PositionSummary> positions)
    {
        if (positions.Count == 0)
        {
            WriteInfo("No open positions.");
            return;
        }

        var table = CreateTable("Deal ID", "Instrument", "Direction", "Size", "Currency", "Stop", "Limit", "Trail Dist", "Trail Inc", "Created");
        foreach (var position in positions)
        {
            table.AddRow(
                position.DealId,
                position.Instrument.Value,
                position.Direction.ToString(),
                CliParsing.FormatDecimal(position.Size),
                position.Currency,
                CliParsing.FormatDecimal(position.StopLevel),
                CliParsing.FormatDecimal(position.LimitLevel),
                CliParsing.FormatDecimal(position.TrailingStopDistance),
                CliParsing.FormatDecimal(position.TrailingStopIncrement),
                CliParsing.FormatDate(position.CreatedAtUtc));
        }

        _console.Write(table);
    }

    public void WriteMarkets(IReadOnlyList<MarketSearchResult> markets)
    {
        if (markets.Count == 0)
        {
            WriteInfo("No matching markets.");
            return;
        }

        var table = CreateTable("Instrument", "Name", "Type", "Expiry", "Currency", "Status");
        foreach (var market in markets)
        {
            table.AddRow(
                market.Instrument.Value,
                market.Name,
                market.Type ?? "n/a",
                market.Expiry ?? "n/a",
                market.CurrencyCode ?? "n/a",
                market.Status.ToString());
        }

        _console.Write(table);
    }

    public void WriteMarketBrowsePage(MarketNavigationPage page)
    {
        WriteKeyValuePanel(
            "Market Node",
            ("Name", page.Name),
            ("Node ID", page.CurrentNodeId ?? "root"));

        if (page.Nodes.Count == 0)
        {
            WriteInfo("No child nodes.");
        }
        else
        {
            var nodesTable = CreateTable("Child Node ID", "Name");
            foreach (var node in page.Nodes)
            {
                nodesTable.AddRow(node.Id, node.Name);
            }

            _console.Write(nodesTable);
        }

        if (page.Markets.Count == 0)
        {
            WriteInfo("No markets in this node.");
            return;
        }

        WriteMarkets(page.Markets);
    }

    public void WritePrices(PriceSeries series)
    {
        if (series.Bars.Count == 0)
        {
            WriteInfo("No prices returned.");
            return;
        }

        WriteKeyValuePanel(
            "Price Series",
            ("Instrument", series.Instrument.Value),
            ("Resolution", series.Resolution?.ToString() ?? "n/a"),
            ("Bars", series.Bars.Count.ToString()));

        var table = CreateTable("Time", "Bid O", "Bid H", "Bid L", "Bid C", "Ask O", "Ask H", "Ask L", "Ask C", "Volume");
        foreach (var bar in series.Bars)
        {
            table.AddRow(
                CliParsing.FormatDate(bar.TimestampUtc),
                CliParsing.FormatDecimal(bar.BidOpen),
                CliParsing.FormatDecimal(bar.BidHigh),
                CliParsing.FormatDecimal(bar.BidLow),
                CliParsing.FormatDecimal(bar.BidClose),
                CliParsing.FormatDecimal(bar.AskOpen),
                CliParsing.FormatDecimal(bar.AskHigh),
                CliParsing.FormatDecimal(bar.AskLow),
                CliParsing.FormatDecimal(bar.AskClose),
                bar.Volume?.ToString() ?? "n/a");
        }

        _console.Write(table);
    }

    public void WriteChartSaved(PriceSeries series, string outputPath)
    {
        WriteKeyValuePanel(
            "Chart Saved",
            ("Instrument", series.Instrument.Value),
            ("Resolution", series.Resolution?.ToString() ?? "n/a"),
            ("Bars", series.Bars.Count.ToString()),
            ("Path", outputPath));
    }

    public void WriteOrders(IReadOnlyList<OrderSummary> orders)
    {
        if (orders.Count == 0)
        {
            WriteInfo("No orders in range.");
            return;
        }

        var table = CreateTable("Reference", "Deal ID", "Instrument", "Direction", "Size", "Status", "Message", "Time");
        foreach (var order in orders)
        {
            table.AddRow(
                order.DealReference,
                order.DealId ?? "n/a",
                order.Instrument?.Value ?? "n/a",
                order.Direction?.ToString() ?? "n/a",
                CliParsing.FormatDecimal(order.Size),
                order.Status.ToString(),
                order.Message ?? "n/a",
                CliParsing.FormatDate(order.TimestampUtc));
        }

        _console.Write(table);
    }

    public void WriteOrderStatus(OrderSummary? status)
    {
        if (status is null)
        {
            WriteInfo("Order not found.");
            return;
        }

        WriteKeyValuePanel(
            "Order Status",
            ("Reference", status.DealReference),
            ("Deal ID", status.DealId ?? "n/a"),
            ("Status", status.Status.ToString()),
            ("Message", status.Message ?? "n/a"),
            ("Time", CliParsing.FormatDate(status.TimestampUtc)));
    }

    public void WriteTradingError(TradingGatewayException exception)
    {
        _console.MarkupLine($"[red]Trading error ({Markup.Escape(exception.ErrorCode.ToString())}): {Markup.Escape(exception.Message)}[/]");
    }

    public void WriteUsageError(string message)
    {
        _console.MarkupLine($"[red]{Markup.Escape(message)}[/]");
    }

    public void WriteUnexpectedError(Exception exception)
    {
        _console.MarkupLine($"[red]Unexpected error ({Markup.Escape(exception.GetType().Name)}): {Markup.Escape(exception.Message)}[/]");
    }

    public void WriteCancellation()
    {
        _console.MarkupLine("[yellow]Command cancelled.[/]");
    }

    public void WriteInfo(string message)
    {
        _console.MarkupLine($"[grey]{Markup.Escape(message)}[/]");
    }

    public void WriteDailyBriefResearch(DateOnly tradingDate, DailyBriefResearchResult result)
    {
        WriteKeyValuePanel(
            "Daily Brief",
            ("Trading Date", tradingDate.ToString("yyyy-MM-dd")),
            ("Generated At", CliParsing.FormatDate(result.CompletedAtUtc)),
            ("Artifact", result.ArtifactPath));
    }

    public void WriteTradingDayPlan(TradingDayPlan plan)
    {
        WriteKeyValuePanel(
            "Trading Day Plan",
            ("Trading Date", plan.TradingDate.ToString("yyyy-MM-dd")),
            ("Regime", plan.MarketRegime.ToString()),
            ("Planned At", CliParsing.FormatDate(plan.PlannedAtUtc)),
            ("Watch List", plan.WatchList.Count.ToString()));

        var table = CreateTable("Rank", "Instrument", "Rationale");
        foreach (var market in plan.WatchList)
        {
            table.AddRow(
                market.Rank.ToString(),
                market.Instrument.Value,
                market.Rationale);
        }

        _console.Write(table);
    }

    private void WriteKeyValuePanel(string title, params (string Key, string Value)[] rows)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders();
        table.AddColumn("Key");
        table.AddColumn("Value");

        foreach (var (key, value) in rows)
        {
            table.AddRow($"[grey]{Markup.Escape(key)}[/]", Markup.Escape(value));
        }

        _console.Write(new Panel(table).Header(title));
    }

    private static Table CreateTable(params string[] columns)
    {
        var table = new Table().RoundedBorder();
        foreach (var column in columns)
        {
            table.AddColumn(column);
        }

        return table;
    }
}
