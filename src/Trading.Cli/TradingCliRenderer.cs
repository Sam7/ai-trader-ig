using Spectre.Console;
using Trading.AI.DailyBriefing;
using Trading.Abstractions;
using Trading.Automation.Execution;
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

    public void WriteMarketDetails(MarketDetails details)
    {
        var rules = details.DealingRules;
        WriteKeyValuePanel(
            "Market Details",
            ("Instrument", details.Instrument.Value),
            ("Name", details.Name),
            ("Status", details.Status.ToString()),
            ("Type", details.Type ?? "n/a"),
            ("Expiry", details.Expiry ?? "n/a"),
            ("Currency", details.CurrencyCode ?? "n/a"),
            ("Bid", CliParsing.FormatDecimal(details.Bid)),
            ("Ask", CliParsing.FormatDecimal(details.Ask)),
            ("Lot Size", CliParsing.FormatDecimal(details.LotSize)),
            ("Unit", details.Unit ?? "n/a"),
            ("Force Open Allowed", FormatBoolean(details.ForceOpenAllowed)),
            ("Stops/Limits Allowed", FormatBoolean(details.StopsLimitsAllowed)),
            ("Controlled Risk Allowed", FormatBoolean(details.ControlledRiskAllowed)),
            ("Streaming Prices Available", FormatBoolean(details.StreamingPricesAvailable)),
            ("Minimum Deal Size", FormatDistance(rules?.MinimumDealSize)),
            ("Minimum Step Distance", FormatDistance(rules?.MinimumStepDistance)),
            ("Minimum Controlled-Risk Stop", FormatDistance(rules?.MinimumControlledRiskStopDistance)),
            ("Minimum Stop/Limit Distance", FormatDistance(rules?.MinimumStopOrLimitDistance)),
            ("Maximum Stop/Limit Distance", FormatDistance(rules?.MaximumStopOrLimitDistance)),
            ("Market Order Preference", rules?.MarketOrderPreference ?? "n/a"),
            ("Trailing Stops Preference", rules?.TrailingStopsPreference ?? "n/a"),
            ("Supported Order Types", details.SupportedOrderTypes.Count == 0 ? "n/a" : string.Join(", ", details.SupportedOrderTypes)));
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
            ("Bars", series.Bars.Count.ToString()),
            ("First", CliParsing.FormatDate(series.Bars[0].TimestampUtc)),
            ("Latest", CliParsing.FormatDate(series.Bars[^1].TimestampUtc)));

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

    public void WriteIntradayOpportunityReview(IntradayOpportunityReviewResult result)
    {
        WriteKeyValuePanel(
            "Intraday Opportunity Scan",
            ("Trading Date", result.TradingDate.ToString("yyyy-MM-dd")),
            ("Reviewed At", CliParsing.FormatDate(result.ReviewedAtUtc)),
            ("Assessments", result.MarketAssessments.Count.ToString()),
            ("Candidates", result.CandidateOpportunities.Count.ToString()),
            ("Outcome", result.Outcome));

        var assessments = CreateTable("Instrument", "Bias", "Score", "Why Now", "Stand Aside");
        foreach (var assessment in result.MarketAssessments)
        {
            assessments.AddRow(
                assessment.Instrument.Value,
                assessment.DirectionalBias.ToString(),
                assessment.OpportunityScore.ToString(),
                assessment.WhyNow,
                string.IsNullOrWhiteSpace(assessment.StandAsideReason) ? "n/a" : assessment.StandAsideReason);
        }

        _console.Write(assessments);

        if (result.CandidateOpportunities.Count == 0)
        {
            WriteInfo("No actionable intraday candidates were returned.");
            return;
        }

        var candidates = CreateTable("Instrument", "Direction", "Score", "Entry", "Stop", "Target", "R:R", "Spread", "Method");
        foreach (var candidate in result.CandidateOpportunities)
        {
            candidates.AddRow(
                candidate.Instrument.Value,
                candidate.Direction.ToString(),
                candidate.OpportunityScore.ToString(),
                CliParsing.FormatDecimal(candidate.EntryPrice),
                CliParsing.FormatDecimal(candidate.StopLossPrice),
                CliParsing.FormatDecimal(candidate.TakeProfitPrice),
                CliParsing.FormatDecimal(candidate.RewardRiskRatio),
                CliParsing.FormatDecimal(candidate.CurrentSpread),
                candidate.EntryMethod.ToString());
        }

        _console.Write(candidates);
    }

    public void WriteIntradayOpportunityPreparation(IntradayOpportunityPreparationDocument preparation)
    {
        WriteKeyValuePanel(
            "Intraday Preparation",
            ("Trading Date", preparation.TradingDate.ToString("yyyy-MM-dd")),
            ("Prepared At", CliParsing.FormatDate(preparation.RequestedAtUtc)),
            ("Prepared JSON", preparation.PreparedArtifact.Path),
            ("Prepared URI", preparation.PreparedArtifact.Uri),
            ("Request Text", preparation.RequestTextArtifact.Path),
            ("Request URI", preparation.RequestTextArtifact.Uri));

        var table = CreateTable("Instrument", "Rank", "Refresh", "Fetched Bars", "Chart Path", "Chart URI");
        foreach (var market in preparation.Markets)
        {
            table.AddRow(
                market.InstrumentName,
                market.Rank.ToString(),
                market.PriceSeriesRefreshMode.ToString(),
                market.FetchedBarCount.ToString(),
                market.ChartArtifact.Path,
                market.ChartArtifact.Uri);
        }

        _console.Write(table);
    }

    public void WriteIntradayOpportunitySubmitResult(IntradayOpportunitySubmitResult result)
    {
        WriteIntradayOpportunityPreparation(result.PreparedRun);
        WriteKeyValuePanel(
            "OpenAI Observability",
            ("Envelope JSON", result.ExecutionArtifacts.PromptEnvelopeArtifact.Path),
            ("Envelope URI", result.ExecutionArtifacts.PromptEnvelopeArtifact.Uri),
            ("Extracted JSON", result.ExecutionArtifacts.ExtractedJsonArtifact.Path),
            ("Extracted URI", result.ExecutionArtifacts.ExtractedJsonArtifact.Uri));

        if (result.ExecutionArtifacts.DecisionAuditArtifact is { } auditArtifact)
        {
            WriteKeyValuePanel(
                "Decision Audit",
                ("Audit JSON", auditArtifact.Path),
                ("Audit URI", auditArtifact.Uri));
        }

        if (result.ExecutionArtifacts.AttachmentArtifacts.Count > 0)
        {
            var attachments = CreateTable("Attachment Path", "Attachment URI");
            foreach (var attachment in result.ExecutionArtifacts.AttachmentArtifacts)
            {
                attachments.AddRow(attachment.Path, attachment.Uri);
            }

            _console.Write(attachments);
        }

        WriteIntradayOpportunityReview(result.WorkflowResult);
    }

    public void WriteDecisionAuditEvaluation(DecisionAuditEvaluationReport report)
    {
        WriteKeyValuePanel(
            "Decision Audit Evaluation",
            ("Root", report.RootPath),
            ("Trading Date", report.TradingDate?.ToString("yyyy-MM-dd") ?? "all"),
            ("Resolution", report.Resolution.ToString()),
            ("Records", report.RecordsEvaluated.ToString()),
            ("Candidates", report.CandidatesEvaluated.ToString()),
            ("Average R", CliParsing.FormatDecimal(report.AverageEstimatedRMultiple)),
            ("Report JSON", report.ReportArtifact?.Path ?? "n/a"));

        if (report.RecordsEvaluated == 0)
        {
            WriteInfo("No decision audit records were found for the requested scope. Run automation run or automation intraday scan first, then evaluate after market data has been collected.");
            return;
        }

        var outcomes = CreateTable("Outcome", "Count");
        outcomes.AddRow(PaperTradeOutcomeStatus.TargetHit.ToString(), report.TargetHitCount.ToString());
        outcomes.AddRow(PaperTradeOutcomeStatus.StoppedOut.ToString(), report.StoppedOutCount.ToString());
        outcomes.AddRow(PaperTradeOutcomeStatus.Expired.ToString(), report.ExpiredCount.ToString());
        outcomes.AddRow(PaperTradeOutcomeStatus.NoFill.ToString(), report.NoFillCount.ToString());
        outcomes.AddRow(PaperTradeOutcomeStatus.DataInsufficient.ToString(), report.DataInsufficientCount.ToString());
        _console.Write(outcomes);

        WriteKeyValuePanel(
            "Decision Bias",
            ("Assessments", report.BiasSummary.AssessmentCount.ToString()),
            ("Assessment Bias", report.BiasSummary.DominantAssessmentDirection),
            ("Assessments Evaluated", report.AssessmentsEvaluated.ToString()),
            ("Followed Bias", report.AssessmentFollowedBiasCount.ToString()),
            ("Against Bias", report.AssessmentMovedAgainstBiasCount.ToString()),
            ("Flat", report.AssessmentFlatCount.ToString()),
            ("Assessment Data Gaps", report.AssessmentDataInsufficientCount.ToString()),
            ("Candidates", report.BiasSummary.CandidateCount.ToString()),
            ("Candidate Bias", report.BiasSummary.DominantCandidateDirection),
            ("Buy Candidates", report.BiasSummary.BuyCandidateCount.ToString()),
            ("Sell Candidates", report.BiasSummary.SellCandidateCount.ToString()));
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

    private static string FormatBoolean(bool? value)
        => value?.ToString() ?? "n/a";

    private static string FormatDistance(MarketRuleDistanceSummary? value)
    {
        if (value is null)
        {
            return "n/a";
        }

        var unit = string.IsNullOrWhiteSpace(value.Unit) ? string.Empty : $" {value.Unit}";
        return $"{CliParsing.FormatDecimal(value.Value)}{unit}";
    }
}
