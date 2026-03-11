using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Testing;
using Trading.Abstractions;
using Trading.Charting;

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
        var gateway = new FakeTradingGateway
        {
            PlaceMarketOrderResult = new PlaceOrderResult(
                "ref-123",
                "deal-456",
                OrderStatus.Accepted,
                "filled",
                DateTimeOffset.Parse("2026-03-10T10:15:00Z"))
        };

        var application = CreateApplication(gateway, console);

        var exitCode = await application.RunAsync(["trades", "buy", "--instrument", "IX.D.SPTRD.DAILY.IP", "--size", "1"]);

        exitCode.Should().Be(0);
        gateway.AuthenticateCalls.Should().Be(1);
        gateway.PlaceMarketOrderRequests.Should().ContainSingle();
        gateway.PlaceMarketOrderRequests[0].Instrument.Value.Should().Be("IX.D.SPTRD.DAILY.IP");
        gateway.PlaceMarketOrderRequests[0].Direction.Should().Be(TradeDirection.Buy);
        gateway.PlaceMarketOrderRequests[0].Size.Should().Be(1);
        console.Output.Should().Contain("Buy Submitted");
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
        FakeAutomationRuntime? automationRuntime = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITradingGateway>(gateway);
        services.AddSingleton<IPriceChartRenderer>(chartRenderer);
        services.AddSingleton<IAnsiConsole>(console);
        services.AddSingleton<IAutomationRuntime>(automationRuntime ?? new FakeAutomationRuntime());
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

    private sealed class FakeTradingGateway : ITradingGateway
    {
        public Exception? AuthenticateException { get; init; }

        public int AuthenticateCalls { get; private set; }

        public List<PlaceOrderRequest> PlaceMarketOrderRequests { get; } = [];

        public List<OrderQuery> OrderQueries { get; } = [];

        public List<GetPricesRequest> GetPricesRequests { get; } = [];

        public PlaceOrderResult PlaceMarketOrderResult { get; init; } = new(
            "ref-default",
            "deal-default",
            OrderStatus.Accepted,
            null,
            DateTimeOffset.Parse("2026-03-10T00:00:00Z"));

        public IReadOnlyList<OrderSummary> OrdersResult { get; init; } = [];

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
        public Task RunAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
