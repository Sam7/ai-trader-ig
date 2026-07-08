using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Testing;
using Trading.Abstractions;
using Trading.Automation.Execution;
using Trading.Charting;
using Trading.Execution;
using Trading.MarketData;

public sealed class TradingCliApplicationTests
{
    [Fact]
    public async Task RunAsync_WithNoArguments_ShouldRenderHelpAndReturnUsageExitCode()
    {
        var console = CreateConsole();
        var application = CreateApplication(new FakeTradingGateway(), console);

        var exitCode = await application.RunAsync([]);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("USAGE");
        console.Output.Should().Contain("automation");
        console.Output.Should().Contain("trades");
        console.Output.Should().Contain("markets");
    }

    [Fact]
    public async Task RunAsync_WithTradeBuyCommand_ShouldAuthenticatePlaceOrderAndRenderResult()
    {
        var console = CreateConsole();
        var gateway = new FakeTradingGateway();
        var execution = new FakeExecutionSubmissionService
        {
            MarketOrderResult = CreateSubmissionResult(
                "manual-buy-1",
                ExecutionOperationKind.MarketOpen,
                "ref-123",
                "deal-456",
                OrderStatus.Accepted,
                "filled")
        };

        var application = CreateApplication(gateway, new FakePriceChartRenderer(), console, executionSubmissionService: execution);

        var exitCode = await application.RunAsync(["trades", "buy", "--instrument", "IX.D.SPTRD.DAILY.IP", "--size", "1", "--operation-id", "manual-buy-1"]);

        exitCode.Should().Be(0);
        gateway.AuthenticateCalls.Should().Be(1);
        execution.MarketOrderRequests.Should().ContainSingle();
        execution.MarketOrderRequests[0].OperationId.Should().Be("manual-buy-1");
        execution.MarketOrderRequests[0].Request.Instrument.Value.Should().Be("IX.D.SPTRD.DAILY.IP");
        execution.MarketOrderRequests[0].Request.Direction.Should().Be(TradeDirection.Buy);
        execution.MarketOrderRequests[0].Request.Size.Should().Be(1);
        console.Output.Should().Contain("Buy Submitted");
        console.Output.Should().Contain("manual-buy-1");
        console.Output.Should().Contain("ref-123");
        console.Output.Should().Contain("deal-456");
    }

    [Fact]
    public async Task RunAsync_WithPricesMaxButNoResolution_ShouldReturnUsageExitCode()
    {
        var console = CreateConsole();
        var application = CreateApplication(new FakeTradingGateway(), console);

        var exitCode = await application.RunAsync(["markets", "prices", "--instrument", "CC.D.VIX.UMA.IP", "--max", "10"]);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Option --resolution is required");
    }

    [Fact]
    public async Task RunAsync_WithMarketDataCollectCommand_ShouldRunCollectorForRequestedMarkets()
    {
        var console = CreateConsole();
        var collector = new FakeMarketDataCollector();
        var application = CreateApplication(new FakeTradingGateway(), new FakePriceChartRenderer(), console, marketDataCollector: collector);

        var exitCode = await application.RunAsync([
            "marketdata",
            "collect",
            "--instruments",
            "CS.D.BITCOIN.CFD.IP,CS.D.CFAGOLD.CFA.IP",
            "--duration",
            "00:00:00",
        ]);

        exitCode.Should().Be(0);
        collector.Requests.Should().ContainSingle();
        collector.Requests[0].Instruments.Select(instrument => instrument.Value)
            .Should().Equal("CS.D.BITCOIN.CFD.IP", "CS.D.CFAGOLD.CFA.IP");
        collector.Requests[0].Duration.Should().Be(TimeSpan.Zero);
        console.Output.Should().Contain("Market-data collector completed");
    }

    [Fact]
    public async Task RunAsync_WithMarketDataCollectWithoutDuration_ShouldRunIndefinitely()
    {
        var console = CreateConsole();
        var collector = new FakeMarketDataCollector();
        var application = CreateApplication(new FakeTradingGateway(), new FakePriceChartRenderer(), console, marketDataCollector: collector);

        var exitCode = await application.RunAsync([
            "marketdata",
            "collect",
            "--instruments",
            "CS.D.BITCOIN.CFD.IP",
        ]);

        exitCode.Should().Be(0);
        collector.Requests.Should().ContainSingle();
        collector.Requests[0].Duration.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_WithMarketDataCollectSixtyHourDuration_ShouldPassSixtyHours()
    {
        var console = CreateConsole();
        var collector = new FakeMarketDataCollector();
        var application = CreateApplication(new FakeTradingGateway(), new FakePriceChartRenderer(), console, marketDataCollector: collector);

        var exitCode = await application.RunAsync([
            "marketdata",
            "collect",
            "--instruments",
            "CS.D.BITCOIN.CFD.IP",
            "--duration",
            "60:00:00",
        ]);

        exitCode.Should().Be(0);
        collector.Requests.Should().ContainSingle();
        collector.Requests[0].Duration.Should().Be(TimeSpan.FromHours(60));
    }

    [Fact]
    public async Task RunAsync_WithMarketDataCollectExplicitDayDuration_ShouldPassDuration()
    {
        var console = CreateConsole();
        var collector = new FakeMarketDataCollector();
        var application = CreateApplication(new FakeTradingGateway(), new FakePriceChartRenderer(), console, marketDataCollector: collector);

        var exitCode = await application.RunAsync([
            "marketdata",
            "collect",
            "--instruments",
            "CS.D.BITCOIN.CFD.IP",
            "--duration",
            "2.12:00:00",
        ]);

        exitCode.Should().Be(0);
        collector.Requests.Should().ContainSingle();
        collector.Requests[0].Duration.Should().Be(TimeSpan.FromHours(60));
    }

    [Fact]
    public async Task RunAsync_WithAutomationRunDuration_ShouldPassBoundedDuration()
    {
        var console = CreateConsole();
        var runtime = new FakeAutomationRuntime();
        var application = CreateApplication(
            new FakeTradingGateway(),
            new FakePriceChartRenderer(),
            console,
            automationRuntime: runtime);

        var exitCode = await application.RunAsync(["automation", "run", "--duration", "08:00:00"]);

        exitCode.Should().Be(0);
        runtime.Requests.Should().ContainSingle();
        runtime.Requests[0].Duration.Should().Be(TimeSpan.FromHours(8));
    }

    [Fact]
    public async Task RunAsync_WithAutomationRunInstruments_ShouldPassInstrumentFilter()
    {
        var console = CreateConsole();
        var runtime = new FakeAutomationRuntime();
        var application = CreateApplication(
            new FakeTradingGateway(),
            new FakePriceChartRenderer(),
            console,
            automationRuntime: runtime);

        var exitCode = await application.RunAsync([
            "automation",
            "run",
            "--duration",
            "00:00:00",
            "--instruments",
            "CC.D.CL.UMA.IP,CS.D.CFAGOLD.CFA.IP",
        ]);

        exitCode.Should().Be(0);
        runtime.Requests.Should().ContainSingle();
        runtime.Requests[0].Instruments.Should().Equal("CC.D.CL.UMA.IP", "CS.D.CFAGOLD.CFA.IP");
    }

    [Fact]
    public async Task RunAsync_WithAutomationRunRoot_ShouldPassObservabilityRoot()
    {
        var console = CreateConsole();
        var runtime = new FakeAutomationRuntime();
        var application = CreateApplication(
            new FakeTradingGateway(),
            new FakePriceChartRenderer(),
            console,
            automationRuntime: runtime);

        var exitCode = await application.RunAsync([
            "automation",
            "run",
            "--duration",
            "00:00:00",
            "--root",
            "Logs/Observability/evidence-check",
        ]);

        exitCode.Should().Be(0);
        runtime.Requests.Should().ContainSingle();
        runtime.Requests[0].ObservabilityRootPath.Should().Be("Logs/Observability/evidence-check");
    }

    [Fact]
    public async Task RunAsync_WithAutomationRunEmptyInstrumentList_ShouldReturnUsageExitCode()
    {
        var console = CreateConsole();
        var runtime = new FakeAutomationRuntime();
        var application = CreateApplication(
            new FakeTradingGateway(),
            new FakePriceChartRenderer(),
            console,
            automationRuntime: runtime);

        var exitCode = await application.RunAsync(["automation", "run", "--instruments", ","]);

        exitCode.Should().Be(1);
        runtime.Requests.Should().BeEmpty();
        console.Output.Should().Contain("Option --instruments must include at least one EPIC.");
    }

    [Fact]
    public async Task RunAsync_WithAutomationRunDurationOverMaximum_ShouldReturnUsageExitCode()
    {
        var console = CreateConsole();
        var runtime = new FakeAutomationRuntime();
        var application = CreateApplication(
            new FakeTradingGateway(),
            new FakePriceChartRenderer(),
            console,
            automationRuntime: runtime);

        var exitCode = await application.RunAsync(["automation", "run", "--duration", "8.00:00:00"]);

        exitCode.Should().Be(1);
        runtime.Requests.Should().BeEmpty();
        console.Output.Should().Contain("Option --duration must be 7 days or less");
    }

    [Fact]
    public async Task RunAsync_WithAutomationAuditEvaluateCommand_ShouldEvaluateAndRenderDecisionAudit()
    {
        var console = CreateConsole();
        var auditService = new FakeDecisionAuditEvaluationService();
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var application = CreateApplication(
                new FakeTradingGateway(),
                new FakePriceChartRenderer(),
                console,
                decisionAuditEvaluationService: auditService);

            var exitCode = await application.RunAsync([
                "automation",
                "audit",
                "evaluate",
                "--root",
                tempDirectory.FullName,
                "--date",
                "2026-03-12",
                "--resolution",
                "5minute",
                "--strict-data",
                "--max-assessment-missing-bars",
                "3",
                "--max-assessment-consecutive-missing-bars",
                "2",
                "--max-assessment-missing-ratio",
                "0.25",
            ]);

            exitCode.Should().Be(0);
            auditService.Requests.Should().ContainSingle();
            auditService.Requests[0].RootPath.Should().Be(tempDirectory.FullName);
            auditService.Requests[0].TradingDate.Should().Be(new DateOnly(2026, 3, 12));
            auditService.Requests[0].Resolution.Should().Be(PriceResolution.FiveMinutes);
            auditService.Requests[0].StrictData.Should().BeTrue();
            auditService.Requests[0].MaxAssessmentInteriorMissingBars.Should().Be(3);
            auditService.Requests[0].MaxAssessmentConsecutiveMissingBars.Should().Be(2);
            auditService.Requests[0].MaxAssessmentMissingRatio.Should().Be(0.25m);
            console.Output.Should().Contain("Decision Audit Evaluation");
            console.Output.Should().Contain("TargetHit");
            console.Output.Should().Contain("Audit Data Quality");
            console.Output.Should().Contain("Decision Bias");
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task RunAsync_WithAutomationAuditEvaluateAndNoRecords_ShouldRenderEmptyState()
    {
        var console = CreateConsole();
        var tempDirectory = Directory.CreateTempSubdirectory();
        var auditService = new FakeDecisionAuditEvaluationService
        {
            ReportFactory = request => new DecisionAuditEvaluationReport(
                request.RootPath,
                request.TradingDate,
                request.Resolution,
                DateTimeOffset.Parse("2026-03-12T11:00:00Z"),
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                null,
                new DecisionBiasSummary(
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    "None",
                    "None",
                    new Dictionary<string, int>(StringComparer.Ordinal)),
                new ArtifactReference(
                    Path.Combine(request.RootPath, "decision-audit-summary.json"),
                    new Uri(Path.GetFullPath(Path.Combine(request.RootPath, "decision-audit-summary.json"))).AbsoluteUri)),
        };

        try
        {
            var application = CreateApplication(
                new FakeTradingGateway(),
                new FakePriceChartRenderer(),
                console,
                decisionAuditEvaluationService: auditService);

            var exitCode = await application.RunAsync([
                "automation",
                "audit",
                "evaluate",
                "--root",
                tempDirectory.FullName,
                "--date",
                "2026-03-12",
            ]);

            exitCode.Should().Be(0);
            console.Output.Should().Contain("No decision audit records were found");
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task RunAsync_WithMarketDataCollectDurationOverMaximum_ShouldReturnUsageExitCode()
    {
        var console = CreateConsole();
        var collector = new FakeMarketDataCollector();
        var application = CreateApplication(new FakeTradingGateway(), new FakePriceChartRenderer(), console, marketDataCollector: collector);

        var exitCode = await application.RunAsync([
            "marketdata",
            "collect",
            "--instruments",
            "CS.D.BITCOIN.CFD.IP",
            "--duration",
            "8.00:00:00",
        ]);

        exitCode.Should().Be(1);
        collector.Requests.Should().BeEmpty();
        console.Output.Should().Contain("Option --duration must be 7 days or less");
    }

    [Fact]
    public async Task RunAsync_WithMarketDataCollectMissingInstruments_ShouldReturnUsageExitCode()
    {
        var console = CreateConsole();
        var application = CreateApplication(new FakeTradingGateway(), console);

        var exitCode = await application.RunAsync(["marketdata", "collect", "--duration", "00:00:00"]);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Missing required option --instruments.");
    }

    [Fact]
    public async Task RunAsync_WithMarketDataCollectEmptyInstrumentList_ShouldReturnUsageExitCode()
    {
        var console = CreateConsole();
        var application = CreateApplication(new FakeTradingGateway(), console);

        var exitCode = await application.RunAsync(["marketdata", "collect", "--instruments", ",", "--duration", "00:00:00"]);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Option --instruments must include at least one EPIC.");
    }

    [Fact]
    public async Task RunAsync_WithMarketDetailsCommand_ShouldFetchAndRenderDetails()
    {
        var console = CreateConsole();
        var gateway = new FakeTradingGateway
        {
            MarketDetailsResult = new MarketDetails(
                new InstrumentId("CS.D.BITCOIN.CFD.IP"),
                "Bitcoin",
                MarketStatus.Tradeable,
                "CURRENCIES",
                "DFB",
                "USD",
                61000m,
                61005m,
                1m,
                "CONTRACTS",
                true,
                true,
                false,
                true,
                new MarketDealingRulesSummary(
                    new MarketRuleDistanceSummary(0.01m, "CONTRACTS"),
                    new MarketRuleDistanceSummary(1m, "POINTS"),
                    null,
                    new MarketRuleDistanceSummary(10m, "POINTS"),
                    null,
                    "AVAILABLE_DEFAULT_ON",
                    "NOT_AVAILABLE"),
                ["MARKET"]),
        };

        var application = CreateApplication(gateway, console);

        var exitCode = await application.RunAsync(["markets", "details", "--instrument", "CS.D.BITCOIN.CFD.IP"]);

        exitCode.Should().Be(0);
        gateway.AuthenticateCalls.Should().Be(1);
        gateway.GetMarketDetailsRequests.Should().ContainSingle();
        gateway.GetMarketDetailsRequests[0].Value.Should().Be("CS.D.BITCOIN.CFD.IP");
        console.Output.Should().Contain("Market Details");
        console.Output.Should().Contain("Bitcoin");
        console.Output.Should().Contain("Tradeable");
        console.Output.Should().Contain("Minimum Stop/Limit Distance");
        console.Output.Should().Contain("10 POINTS");
    }

    [Fact]
    public async Task RunAsync_WithMarketDetailsCommandMissingInstrument_ShouldReturnUsageExitCode()
    {
        var console = CreateConsole();
        var application = CreateApplication(new FakeTradingGateway(), console);

        var exitCode = await application.RunAsync(["markets", "details"]);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Missing required option --instrument.");
    }

    [Fact]
    public async Task RunAsync_WithMarketDetailsCommandMalformedInstrument_ShouldReturnUsageExitCode()
    {
        var console = CreateConsole();
        var application = CreateApplication(new FakeTradingGateway(), console);

        var exitCode = await application.RunAsync(["markets", "details", "--instrument", "bad epic"]);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Option --instrument must be a single EPIC without whitespace.");
    }

    [Fact]
    public async Task RunAsync_WithMarketDetailsBrokerError_ShouldReturnTradingExitCode()
    {
        var console = CreateConsole();
        var gateway = new FakeTradingGateway
        {
            MarketDetailsException = new TradingGatewayException(TradingErrorCode.InvalidInstrument, "Market not found")
        };

        var application = CreateApplication(gateway, console);

        var exitCode = await application.RunAsync(["markets", "details", "--instrument", "BAD.EPIC"]);

        exitCode.Should().Be(2);
        console.Output.Should().Contain("Trading error");
        console.Output.Should().Contain("Market not found");
    }

    [Fact]
    public async Task RunAsync_WithMarketPricesCommand_ShouldRenderFirstAndLatestTimestamps()
    {
        var console = CreateConsole();
        var gateway = new FakeTradingGateway
        {
            PricesResult = new PriceSeries(
                new InstrumentId("CS.D.BITCOIN.CFD.IP"),
                PriceResolution.TenMinutes,
                [
                    new PriceBar(
                        DateTimeOffset.Parse("2026-06-27T12:40:00Z"),
                        10m,
                        12m,
                        9m,
                        11m,
                        10.5m,
                        12.5m,
                        9.5m,
                        11.5m,
                        100),
                    new PriceBar(
                        DateTimeOffset.Parse("2026-06-27T12:50:00Z"),
                        11m,
                        13m,
                        10m,
                        12m,
                        11.5m,
                        13.5m,
                        10.5m,
                        12.5m,
                        120),
                ])
        };

        var application = CreateApplication(gateway, console);

        var exitCode = await application.RunAsync(["markets", "prices", "--instrument", "CS.D.BITCOIN.CFD.IP", "--resolution", "10minute", "--max", "2"]);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("First");
        console.Output.Should().Contain("2026-06-27T12:40:00.0000000+00:00");
        console.Output.Should().Contain("Latest");
        console.Output.Should().Contain("2026-06-27T12:50:00.0000000+00:00");
    }

    [Fact]
    public async Task RunAsync_WithMarketChartCommand_ShouldFetchRenderAndSaveChart()
    {
        var console = CreateConsole();
        var gateway = new FakeTradingGateway
        {
            PricesResult = new PriceSeries(
                new InstrumentId("CC.D.VIX.UMA.IP"),
                PriceResolution.Hour,
                [
                    new PriceBar(
                        DateTimeOffset.Parse("2026-03-10T00:00:00Z"),
                        10m,
                        12m,
                        9m,
                        11m,
                        10.5m,
                        12.5m,
                        9.5m,
                        11.5m,
                        100),
                    new PriceBar(
                        DateTimeOffset.Parse("2026-03-10T01:00:00Z"),
                        11m,
                        13m,
                        10m,
                        12m,
                        11.5m,
                        13.5m,
                        10.5m,
                        12.5m,
                        120),
                ])
        };
        var chartRenderer = new FakePriceChartRenderer
        {
            RenderedBytes = [1, 2, 3, 4],
        };

        var tempDirectory = Directory.CreateTempSubdirectory();
        var outputPath = Path.Combine(tempDirectory.FullName, "chart.png");

        try
        {
            var application = CreateApplication(gateway, chartRenderer, console);

            var exitCode = await application.RunAsync(
                ["markets", "chart", "--instrument", "CC.D.VIX.UMA.IP", "--resolution", "hour", "--max", "2", "--output", outputPath, "--style", "ohlc", "--gaps", "preserve", "--sma", "3,5", "--bollinger", "4"]);

            exitCode.Should().Be(0);
            gateway.AuthenticateCalls.Should().Be(1);
            gateway.GetPricesRequests.Should().ContainSingle();
            gateway.GetPricesRequests[0].Instrument.Value.Should().Be("CC.D.VIX.UMA.IP");
            gateway.GetPricesRequests[0].Resolution.Should().Be(PriceResolution.Hour);
            gateway.GetPricesRequests[0].MaxPoints.Should().Be(2);
            chartRenderer.Calls.Should().ContainSingle();
            chartRenderer.Calls[0].Style.Should().Be(PriceChartStyle.Ohlc);
            chartRenderer.Calls[0].GapMode.Should().Be(PriceGapMode.Preserve);
            chartRenderer.Calls[0].SimpleMovingAverageWindows.Should().Equal(3, 5);
            chartRenderer.Calls[0].BollingerPeriod.Should().Be(4);
            File.ReadAllBytes(outputPath).Should().Equal(chartRenderer.RenderedBytes);
            console.Output.Should().Contain("Chart Saved");
            console.Output.Should().Contain("CC.D.VIX.UMA.IP");
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task RunAsync_WithMarketChartCommandAndNoPrices_ShouldReturnUsageExitCodeWithoutSavingFile()
    {
        var console = CreateConsole();
        var gateway = new FakeTradingGateway
        {
            PricesResult = new PriceSeries(
                new InstrumentId("CC.D.VIX.UMA.IP"),
                PriceResolution.Hour,
                []),
        };
        var tempDirectory = Directory.CreateTempSubdirectory();
        var outputPath = Path.Combine(tempDirectory.FullName, "chart.png");

        try
        {
            var application = CreateApplication(gateway, new FakePriceChartRenderer(), console);

            var exitCode = await application.RunAsync(
                ["markets", "chart", "--instrument", "CC.D.VIX.UMA.IP", "--resolution", "hour", "--max", "2", "--output", outputPath]);

            exitCode.Should().Be(1);
            File.Exists(outputPath).Should().BeFalse();
            console.Output.Should().Contain("No prices returned for the requested range.");
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public async Task RunAsync_WithEmptyPositions_ShouldRenderEmptyState()
    {
        var console = CreateConsole();
        var application = CreateApplication(new FakeTradingGateway(), console);

        var exitCode = await application.RunAsync(["positions", "list"]);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("No open positions.");
    }

    [Fact]
    public async Task RunAsync_WithOrdersList_ShouldRenderOrderTable()
    {
        var console = CreateConsole();
        var gateway = new FakeTradingGateway
        {
            OrdersResult =
            [
                new OrderSummary(
                    "ref-1",
                    "deal-1",
                    new InstrumentId("IX.D.SPTRD.DAILY.IP"),
                    TradeDirection.Sell,
                    2,
                    OrderStatus.Accepted,
                    "ok",
                    DateTimeOffset.Parse("2026-03-10T12:00:00Z"))
            ]
        };

        var application = CreateApplication(gateway, console);

        var exitCode = await application.RunAsync(["orders", "list", "--max", "5"]);

        exitCode.Should().Be(0);
        gateway.OrderQueries.Should().ContainSingle();
        gateway.OrderQueries[0].MaxItems.Should().Be(5);
        console.Output.Should().Contain("ref-1");
        console.Output.Should().Contain("IX.D.SPT");
    }

    [Fact]
    public async Task RunAsync_WhenGatewayThrowsTradingError_ShouldReturnTradingExitCode()
    {
        var console = CreateConsole();
        var gateway = new FakeTradingGateway
        {
            AuthenticateException = new TradingGatewayException(TradingErrorCode.AuthenticationFailed, "bad credentials")
        };

        var application = CreateApplication(gateway, console);

        var exitCode = await application.RunAsync(["positions", "list"]);

        exitCode.Should().Be(2);
        console.Output.Should().Contain("Trading error");
        console.Output.Should().Contain("bad credentials");
    }

    [Fact]
    public async Task RunAsync_WhenGatewayThrowsUnexpectedError_ShouldReturnUnexpectedExitCode()
    {
        var console = CreateConsole();
        var gateway = new FakeTradingGateway
        {
            AuthenticateException = new InvalidOperationException("boom")
        };

        var application = CreateApplication(gateway, console);

        var exitCode = await application.RunAsync(["positions", "list"]);

        exitCode.Should().Be(99);
        console.Output.Should().Contain("Unexpected error");
        console.Output.Should().Contain("InvalidOperationException");
        console.Output.Should().Contain("boom");
    }

    [Fact]
    public async Task RunAsync_WhenChartRendererThrowsArgumentException_ShouldReturnUnexpectedExitCode()
    {
        var console = CreateConsole();
        var gateway = new FakeTradingGateway
        {
            PricesResult = new PriceSeries(
                new InstrumentId("CC.D.VIX.UMA.IP"),
                PriceResolution.Hour,
                [
                    new PriceBar(
                        DateTimeOffset.Parse("2026-03-10T00:00:00Z"),
                        10m,
                        12m,
                        9m,
                        11m,
                        10.5m,
                        12.5m,
                        9.5m,
                        11.5m,
                        100),
                    new PriceBar(
                        DateTimeOffset.Parse("2026-03-10T01:00:00Z"),
                        11m,
                        13m,
                        10m,
                        12m,
                        11.5m,
                        13.5m,
                        10.5m,
                        12.5m,
                        120),
                ])
        };
        var chartRenderer = new FakePriceChartRenderer
        {
            RenderException = new ArgumentException("broken renderer"),
        };

        var tempDirectory = Directory.CreateTempSubdirectory();
        var outputPath = Path.Combine(tempDirectory.FullName, "chart.png");

        try
        {
            var application = CreateApplication(gateway, chartRenderer, console);

            var exitCode = await application.RunAsync(
                ["markets", "chart", "--instrument", "CC.D.VIX.UMA.IP", "--resolution", "hour", "--max", "2", "--output", outputPath]);

            exitCode.Should().Be(99);
            console.Output.Should().Contain("Unexpected error");
            console.Output.Should().Contain("ArgumentException");
            console.Output.Should().Contain("broken renderer");
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    private static TradingCliApplication CreateApplication(FakeTradingGateway gateway, TestConsole console)
        => CreateApplication(gateway, new FakePriceChartRenderer(), console, new FakeAutomationRuntime());

    private static TradingCliApplication CreateApplication(
        FakeTradingGateway gateway,
        FakePriceChartRenderer chartRenderer,
        TestConsole console,
        FakeAutomationRuntime? automationRuntime = null,
        FakeMarketDataCollector? marketDataCollector = null,
        FakeDecisionAuditEvaluationService? decisionAuditEvaluationService = null,
        FakeExecutionSubmissionService? executionSubmissionService = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITradingGateway>(gateway);
        services.AddSingleton<IPriceChartRenderer>(chartRenderer);
        services.AddSingleton<IAnsiConsole>(console);
        services.AddSingleton<IAutomationRuntime>(automationRuntime ?? new FakeAutomationRuntime());
        services.AddSingleton<IMarketDataCollector>(marketDataCollector ?? new FakeMarketDataCollector());
        services.AddSingleton<IDecisionAuditEvaluationService>(decisionAuditEvaluationService ?? new FakeDecisionAuditEvaluationService());
        services.AddSingleton<IExecutionSubmissionService>(executionSubmissionService ?? new FakeExecutionSubmissionService());
        services.AddTradingCli();

        return new TradingCliApplication(services, console);
    }

    private static TestConsole CreateConsole()
    {
        return new TestConsole
        {
            EmitAnsiSequences = false,
        };
    }

    private static ExecutionSubmissionResult CreateSubmissionResult(
        string operationId,
        ExecutionOperationKind kind,
        string dealReference,
        string? dealId,
        OrderStatus status,
        string? message)
    {
        var timestamp = DateTimeOffset.Parse("2026-03-10T10:15:00Z");
        var state = status switch
        {
            OrderStatus.Rejected => ExecutionBoundaryState.BrokerRejected,
            OrderStatus.Closed => ExecutionBoundaryState.Closed,
            OrderStatus.Accepted or OrderStatus.Open when !string.IsNullOrWhiteSpace(dealId) => ExecutionBoundaryState.Confirmed,
            _ => ExecutionBoundaryState.Submitted,
        };
        var record = new ExecutionOperationRecord(
            operationId,
            kind,
            ExecutionOperationSource.ManualCli,
            state,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            dealReference,
            dealId,
            status,
            timestamp,
            timestamp,
            timestamp,
            state == ExecutionBoundaryState.Confirmed ? timestamp : null,
            state == ExecutionBoundaryState.Closed ? timestamp : null,
            1,
            message);

        return new ExecutionSubmissionResult(record, dealReference, dealId, status, message, timestamp);
    }

    private sealed class FakeExecutionSubmissionService : IExecutionSubmissionService
    {
        public List<MarketOrderExecutionRequest> MarketOrderRequests { get; } = [];

        public ExecutionSubmissionResult? MarketOrderResult { get; init; }

        public Task<ExecutionSubmissionResult> SubmitMarketOrderAsync(
            string operationId,
            ExecutionOperationSource source,
            PlaceOrderRequest request,
            CancellationToken cancellationToken = default)
        {
            MarketOrderRequests.Add(new MarketOrderExecutionRequest(operationId, source, request));
            return Task.FromResult(MarketOrderResult ?? CreateSubmissionResult(
                operationId,
                ExecutionOperationKind.MarketOpen,
                "ref-default",
                "deal-default",
                OrderStatus.Accepted,
                null));
        }

        public Task<ExecutionSubmissionResult> SubmitClosePositionAsync(
            string operationId,
            ExecutionOperationSource source,
            ClosePositionRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CreateSubmissionResult(
                operationId,
                ExecutionOperationKind.PositionClose,
                "close-ref",
                request.DealId,
                OrderStatus.Accepted,
                null));

        public Task<ExecutionSubmissionResult> SubmitUpdatePositionAsync(
            string operationId,
            ExecutionOperationSource source,
            UpdatePositionRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CreateSubmissionResult(
                operationId,
                ExecutionOperationKind.PositionUpdate,
                "update-ref",
                request.DealId,
                OrderStatus.Accepted,
                null));

        public Task<ExecutionSubmissionResult> SubmitCreateWorkingOrderAsync(
            string operationId,
            ExecutionOperationSource source,
            CreateWorkingOrderRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CreateSubmissionResult(
                operationId,
                ExecutionOperationKind.WorkingOrderCreate,
                "working-ref",
                "working-deal",
                OrderStatus.Accepted,
                null));

        public Task<ExecutionSubmissionResult> SubmitUpdateWorkingOrderAsync(
            string operationId,
            ExecutionOperationSource source,
            UpdateWorkingOrderRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CreateSubmissionResult(
                operationId,
                ExecutionOperationKind.WorkingOrderUpdate,
                "working-update-ref",
                request.DealId,
                OrderStatus.Accepted,
                null));

        public Task<ExecutionSubmissionResult> SubmitCancelWorkingOrderAsync(
            string operationId,
            ExecutionOperationSource source,
            string dealId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CreateSubmissionResult(
                operationId,
                ExecutionOperationKind.WorkingOrderCancel,
                "working-cancel-ref",
                dealId,
                OrderStatus.Accepted,
                null));

    }

    private sealed record MarketOrderExecutionRequest(
        string OperationId,
        ExecutionOperationSource Source,
        PlaceOrderRequest Request);

    private sealed class FakeTradingGateway : ITradingGateway
    {
        public Exception? AuthenticateException { get; init; }

        public int AuthenticateCalls { get; private set; }

        public List<PlaceOrderRequest> PlaceMarketOrderRequests { get; } = [];

        public List<OrderQuery> OrderQueries { get; } = [];

        public List<GetPricesRequest> GetPricesRequests { get; } = [];

        public List<InstrumentId> GetMarketDetailsRequests { get; } = [];

        public PlaceOrderResult PlaceMarketOrderResult { get; init; } = new(
            "ref-default",
            "deal-default",
            OrderStatus.Accepted,
            null,
            DateTimeOffset.Parse("2026-03-10T00:00:00Z"));

        public IReadOnlyList<OrderSummary> OrdersResult { get; init; } = [];

        public Exception? MarketDetailsException { get; init; }

        public MarketDetails MarketDetailsResult { get; init; } = new(
            new InstrumentId("CC.D.VIX.UMA.IP"),
            "Volatility Index",
            MarketStatus.Tradeable,
            "INDICES",
            "-",
            "USD",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            []);

        public PriceSeries PricesResult { get; init; } = new(
            new InstrumentId("CC.D.VIX.UMA.IP"),
            PriceResolution.Hour,
            []);

        public Task<ITradingSession> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            AuthenticateCalls++;
            if (AuthenticateException is not null)
            {
                throw AuthenticateException;
            }

            return Task.FromResult<ITradingSession>(new FakeTradingSession("demo-account", "IG Demo", DateTimeOffset.Parse("2026-03-10T00:00:00Z")));
        }

        public Task<PlaceOrderResult> PlaceMarketOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default)
        {
            PlaceMarketOrderRequests.Add(request);
            return Task.FromResult(PlaceMarketOrderResult);
        }

        public Task<WorkingOrderResult> PlaceWorkingOrderAsync(CreateWorkingOrderRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkingOrderResult("working-ref", "working-deal", OrderStatus.Accepted, null, DateTimeOffset.Parse("2026-03-10T00:00:00Z")));

        public Task<ClosePositionResult> ClosePositionAsync(ClosePositionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ClosePositionResult("close-ref", request.DealId, OrderStatus.Accepted, null, DateTimeOffset.Parse("2026-03-10T00:00:00Z")));

        public Task<UpdatePositionResult> UpdatePositionAsync(UpdatePositionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new UpdatePositionResult("update-ref", request.DealId, OrderStatus.Accepted, null, DateTimeOffset.Parse("2026-03-10T00:00:00Z")));

        public Task<WorkingOrderResult> UpdateWorkingOrderAsync(UpdateWorkingOrderRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkingOrderResult("working-update-ref", request.DealId, OrderStatus.Accepted, null, DateTimeOffset.Parse("2026-03-10T00:00:00Z")));

        public Task<WorkingOrderResult> CancelWorkingOrderAsync(string dealId, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkingOrderResult("working-cancel-ref", dealId, OrderStatus.Accepted, null, DateTimeOffset.Parse("2026-03-10T00:00:00Z")));

        public Task<IReadOnlyList<PositionSummary>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PositionSummary>>([]);

        public Task<IReadOnlyList<WorkingOrderSummary>> GetWorkingOrdersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WorkingOrderSummary>>([]);

        public Task<IReadOnlyList<MarketSearchResult>> SearchMarketsAsync(string searchTerm, int maxResults = 20, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MarketSearchResult>>([]);

        public Task<MarketDetails> GetMarketDetailsAsync(InstrumentId instrument, CancellationToken cancellationToken = default)
        {
            if (MarketDetailsException is not null)
            {
                throw MarketDetailsException;
            }

            GetMarketDetailsRequests.Add(instrument);
            return Task.FromResult(MarketDetailsResult);
        }

        public Task<MarketNavigationPage> BrowseMarketsAsync(string? nodeId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new MarketNavigationPage(nodeId, "Root", [], []));

        public Task<PriceSeries> GetPricesAsync(GetPricesRequest request, CancellationToken cancellationToken = default)
        {
            GetPricesRequests.Add(request);
            return Task.FromResult(PricesResult);
        }

        public Task<IReadOnlyList<OrderSummary>> GetOrdersAsync(OrderQuery query, CancellationToken cancellationToken = default)
        {
            OrderQueries.Add(query);
            return Task.FromResult(OrdersResult);
        }

        public Task<OrderSummary?> GetOrderStatusAsync(string dealReference, CancellationToken cancellationToken = default)
            => Task.FromResult<OrderSummary?>(null);
    }

    private sealed class FakeTradingSession : ITradingSession
    {
        public FakeTradingSession(string accountId, string brokerName, DateTimeOffset authenticatedAtUtc)
        {
            AccountId = accountId;
            BrokerName = brokerName;
            AuthenticatedAtUtc = authenticatedAtUtc;
        }

        public string AccountId { get; }

        public string BrokerName { get; }

        public DateTimeOffset AuthenticatedAtUtc { get; }
    }

    private sealed class FakePriceChartRenderer : IPriceChartRenderer
    {
        public List<RenderCall> Calls { get; } = [];

        public Exception? RenderException { get; init; }

        public byte[] RenderedBytes { get; init; } = [137, 80, 78, 71];

        public byte[] RenderPng(
            PriceSeries series,
            PriceChartStyle style = PriceChartStyle.Candlestick,
            PriceGapMode gapMode = PriceGapMode.Compress,
            IReadOnlyList<int>? simpleMovingAverageWindows = null,
            int? bollingerPeriod = null,
            int width = 1200,
            int height = 800)
        {
            if (RenderException is not null)
            {
                throw RenderException;
            }

            Calls.Add(new RenderCall(
                series,
                style,
                gapMode,
                simpleMovingAverageWindows ?? [],
                bollingerPeriod,
                width,
                height));

            return RenderedBytes;
        }
    }

    private sealed record RenderCall(
        PriceSeries Series,
        PriceChartStyle Style,
        PriceGapMode GapMode,
        IReadOnlyList<int> SimpleMovingAverageWindows,
        int? BollingerPeriod,
        int Width,
        int Height);

    private sealed class FakeAutomationRuntime : IAutomationRuntime
    {
        public List<RunRequest> Requests { get; } = [];

        public Task RunAsync(
            TimeSpan? duration = null,
            IReadOnlyList<string>? instruments = null,
            string? observabilityRootPath = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(new RunRequest(duration, instruments ?? [], observabilityRootPath));
            return Task.CompletedTask;
        }
    }

    private sealed record RunRequest(
        TimeSpan? Duration,
        IReadOnlyList<string> Instruments,
        string? ObservabilityRootPath);

    private sealed class FakeMarketDataCollector : IMarketDataCollector
    {
        public List<CollectRequest> Requests { get; } = [];

        public Task RunAsync(
            IReadOnlyList<InstrumentId> instruments,
            TimeSpan? duration,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(new CollectRequest(instruments, duration));
            return Task.CompletedTask;
        }
    }

    private sealed record CollectRequest(
        IReadOnlyList<InstrumentId> Instruments,
        TimeSpan? Duration);

    private sealed class FakeDecisionAuditEvaluationService : IDecisionAuditEvaluationService
    {
        public List<DecisionAuditEvaluationRequest> Requests { get; } = [];

        public Func<DecisionAuditEvaluationRequest, DecisionAuditEvaluationReport>? ReportFactory { get; init; }

        public Task<DecisionAuditEvaluationReport> EvaluateAsync(
            DecisionAuditEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (ReportFactory is not null)
            {
                return Task.FromResult(ReportFactory(request));
            }

            return Task.FromResult(new DecisionAuditEvaluationReport(
                request.RootPath,
                request.TradingDate,
                request.Resolution,
                DateTimeOffset.Parse("2026-03-12T11:00:00Z"),
                2,
                3,
                1,
                1,
                0,
                1,
                0,
                6,
                4,
                2,
                0,
                0,
                0.25m,
                new DecisionBiasSummary(
                    6,
                    3,
                    4,
                    2,
                    2,
                    1,
                    "Buy",
                    "Buy",
                    new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["CC.D.TEST.IP"] = 3,
                    }),
                new ArtifactReference(
                    Path.Combine(request.RootPath, "decision-audit-summary.json"),
                    new Uri(Path.GetFullPath(Path.Combine(request.RootPath, "decision-audit-summary.json"))).AbsoluteUri)));
        }
    }
}
