using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Testing;
using Trading.Abstractions;

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
        console.Output.Should().Contain("boom");
    }

    private static TradingCliApplication CreateApplication(FakeTradingGateway gateway, TestConsole console)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITradingGateway>(gateway);
        services.AddSingleton<IAnsiConsole>(console);
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

        public PlaceOrderResult PlaceMarketOrderResult { get; init; } = new(
            "ref-default",
            "deal-default",
            OrderStatus.Accepted,
            null,
            DateTimeOffset.Parse("2026-03-10T00:00:00Z"));

        public IReadOnlyList<OrderSummary> OrdersResult { get; init; } = [];

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
            => Task.FromResult(new PriceSeries(request.Instrument, request.Resolution, []));

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
}
