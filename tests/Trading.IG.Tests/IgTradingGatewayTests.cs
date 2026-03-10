using FluentAssertions;
using Ig.Trading.Sdk;
using Ig.Trading.Sdk.Auth;
using Ig.Trading.Sdk.Errors;
using Ig.Trading.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using Trading.Abstractions;
using Trading.IG;

namespace Trading.IG.Tests;

public class IgTradingGatewayTests
{
    [Fact]
    public async Task GetOrderStatusAsync_WithRejectedConfirmation_ShouldMapToRejected()
    {
        var api = new FakeIgTradingApi
        {
            DealConfirmation = _ => Task.FromResult<DealConfirmationResponse?>(new DealConfirmationResponse(
                "REJECTED",
                "REJECTED",
                "MARKET_CLOSED",
                "D1",
                "REF1",
                "IX.D.SPTRD.DAILY.IP",
                "BUY",
                1m,
                DateTimeOffset.UtcNow.ToString("O"))),
        };

        var gateway = CreateGateway(api);

        var status = await gateway.GetOrderStatusAsync("REF1");

        status.Should().NotBeNull();
        status!.Status.Should().Be(OrderStatus.Rejected);
        status.Message.Should().Be("MARKET_CLOSED");
    }

    [Fact]
    public async Task PlaceMarketOrderAsync_WhenMarketNotTradeable_ShouldReturnMarketClosedError()
    {
        var api = new FakeIgTradingApi
        {
            Market = _ => Task.FromResult(new MarketDetailsResponse(
                new MarketInstrument("IX.D.SPTRD.DAILY.IP", "DFB", [new MarketCurrency("USD", true)]),
                new MarketSnapshot("CLOSED"))),
        };

        var gateway = CreateGateway(api);

        var action = () => gateway.PlaceMarketOrderAsync(new PlaceOrderRequest(new InstrumentId("IX.D.SPTRD.DAILY.IP"), TradeDirection.Buy, 1m));

        var exception = await action.Should().ThrowAsync<TradingGatewayException>();
        exception.Which.ErrorCode.Should().Be(TradingErrorCode.MarketClosed);
    }

    [Fact]
    public async Task GetOpenPositionsAsync_WhenSdkThrowsMarginError_ShouldTranslate()
    {
        var api = new FakeIgTradingApi
        {
            OpenPositions = _ => throw new IgApiException("error.public-api.exceeded-account-trading-allowance", HttpStatusCode.BadRequest, "failed"),
        };

        var gateway = CreateGateway(api);

        var action = () => gateway.GetOpenPositionsAsync();

        var exception = await action.Should().ThrowAsync<TradingGatewayException>();
        exception.Which.ErrorCode.Should().Be(TradingErrorCode.BrokerError);
    }

    private static IgTradingGateway CreateGateway(IIgTradingApi api)
        => new(api, NullLogger<IgTradingGateway>.Instance);

    private sealed class FakeIgTradingApi : IIgTradingApi
    {
        public Func<CancellationToken, Task<IgSessionContext>> Authenticate { get; set; }
            = _ => Task.FromResult(new IgSessionContext("cst", "token", "ACC1", DateTimeOffset.UtcNow));

        public Func<string, Task<MarketDetailsResponse>> Market { get; set; }
            = _ => Task.FromResult(new MarketDetailsResponse(new MarketInstrument("IX.D.SPTRD.DAILY.IP", "DFB", [new MarketCurrency("USD", true)]), new MarketSnapshot("TRADEABLE")));

        public Func<CreatePositionRequest, Task<CreatePositionResponse>> CreatePosition { get; set; }
            = _ => Task.FromResult(new CreatePositionResponse("REF-NEW"));

        public Func<Ig.Trading.Sdk.Models.ClosePositionRequest, Task<ClosePositionResponse>> ClosePosition { get; set; }
            = _ => Task.FromResult(new ClosePositionResponse("REF-CLOSE"));

        public Func<CancellationToken, Task<PositionsResponse>> OpenPositions { get; set; }
            = _ => Task.FromResult(new PositionsResponse([]));

        public Func<string, Task<DealConfirmationResponse?>> DealConfirmation { get; set; }
            = _ => Task.FromResult<DealConfirmationResponse?>(null);

        public Func<CancellationToken, Task<WorkingOrdersResponse>> WorkingOrders { get; set; }
            = _ => Task.FromResult(new WorkingOrdersResponse([]));

        public Func<DateTimeOffset, DateTimeOffset, int, Task<ActivityResponse>> Activity { get; set; }
            = (_, _, _) => Task.FromResult(new ActivityResponse([]));

        public Task<IgSessionContext> AuthenticateAsync(CancellationToken cancellationToken = default) => Authenticate(cancellationToken);

        public Task<MarketDetailsResponse> GetMarketByEpicAsync(string epic, CancellationToken cancellationToken = default) => Market(epic);

        public Task<CreatePositionResponse> CreatePositionAsync(CreatePositionRequest request, CancellationToken cancellationToken = default) => CreatePosition(request);

        public Task<ClosePositionResponse> ClosePositionAsync(Ig.Trading.Sdk.Models.ClosePositionRequest request, CancellationToken cancellationToken = default) => ClosePosition(request);

        public Task<PositionsResponse> GetOpenPositionsAsync(CancellationToken cancellationToken = default) => OpenPositions(cancellationToken);

        public Task<DealConfirmationResponse?> GetDealConfirmationAsync(string dealReference, CancellationToken cancellationToken = default) => DealConfirmation(dealReference);

        public Task<WorkingOrdersResponse> GetWorkingOrdersAsync(CancellationToken cancellationToken = default) => WorkingOrders(cancellationToken);

        public Task<ActivityResponse> GetActivityAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, int pageSize, CancellationToken cancellationToken = default)
            => Activity(fromUtc, toUtc, pageSize);
    }
}
