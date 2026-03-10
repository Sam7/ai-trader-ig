using FluentAssertions;
using Ig.Trading.Sdk;
using Ig.Trading.Sdk.Auth;
using Ig.Trading.Sdk.Errors;
using Ig.Trading.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using Trading.Abstractions;
using Trading.IG;
using CreateWorkingOrderRequest = Trading.Abstractions.CreateWorkingOrderRequest;
using UpdateWorkingOrderRequest = Trading.Abstractions.UpdateWorkingOrderRequest;

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

    [Fact]
    public async Task PlaceMarketOrderAsync_ShouldSubmitIgSafeDealReference()
    {
        CreatePositionRequest? capturedRequest = null;

        var api = new FakeIgTradingApi
        {
            CreatePosition = request =>
            {
                capturedRequest = request;
                return Task.FromResult(new CreatePositionResponse("REF-NEW"));
            },
        };

        var gateway = CreateGateway(api);

        await gateway.PlaceMarketOrderAsync(new PlaceOrderRequest(new InstrumentId("IX.D.SPTRD.DAILY.IP"), TradeDirection.Buy, 1m));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.DealReference.Should().MatchRegex("^[A-Z0-9]{1,30}$");
    }

    [Fact]
    public async Task GetOrderStatusAsync_ShouldUseActivityDetailsDealReferenceForClosedPositions()
    {
        var api = new FakeIgTradingApi
        {
            Activity = (_, _, _) => Task.FromResult(new ActivityResponse(
            [
                new ActivityItem(
                    null,
                    DateTimeOffset.UtcNow.ToString("O"),
                    new ActivityDetails(
                        "CLOSEABC123",
                        [new ActivityAction("POSITION_CLOSED", "DIAAAAR5SV9Q3A4")],
                        null,
                        "SELL",
                        10m,
                        23.49m,
                        "AUD"),
                    "ACCEPTED",
                    "Position/s closed",
                    "DIAAAAWTY7AF8AV",
                    "CC.D.VIX.UMA.IP",
                    null)
            ])),
        };

        var gateway = CreateGateway(api);

        var status = await gateway.GetOrderStatusAsync("CLOSEABC123");

        status.Should().NotBeNull();
        status!.Status.Should().Be(OrderStatus.Closed);
        status.DealReference.Should().Be("CLOSEABC123");
        status.DealId.Should().Be("DIAAAAWTY7AF8AV");
    }

    [Fact]
    public async Task GetOrderStatusAsync_ShouldUseTransactionsAsFallbackForClosedDeals()
    {
        var api = new FakeIgTradingApi
        {
            Transactions = _ => Task.FromResult(new TransactionHistoryResponse(
            [
                new TransactionItem(
                    "2026-03-10",
                    DateTimeOffset.UtcNow.ToString("O"),
                    null,
                    "Volatility Index",
                    "-",
                    "A$54.00",
                    "DEAL",
                    "TY7AF8AV",
                    null,
                    null,
                    "+10",
                    "A$",
                    false)
            ],
            new TransactionMetadata(1, new TransactionPageData(20, 1, 1)))),
        };

        var gateway = CreateGateway(api);

        var status = await gateway.GetOrderStatusAsync("DIAAAAWTY7AF8AV");

        status.Should().NotBeNull();
        status!.Status.Should().Be(OrderStatus.Closed);
        status.DealId.Should().Be("DIAAAAWTY7AF8AV");
    }

    [Fact]
    public async Task GetOrderStatusAsync_ShouldUseJournalToCorrelateCloseSubmission()
    {
        var api = new FakeIgTradingApi
        {
            Activity = (_, _, _) => Task.FromResult(new ActivityResponse(
            [
                new ActivityItem(
                    null,
                    DateTimeOffset.UtcNow.ToString("O"),
                    new ActivityDetails(
                        "BROKERREF123",
                        [new ActivityAction("POSITION_CLOSED", "OPENDEAL1")],
                        null,
                        "SELL",
                        2m,
                        23.49m,
                        "AUD"),
                    "ACCEPTED",
                    "Position/s closed",
                    "DIAAAAWBROKER1",
                    "CC.D.VIX.UMA.IP",
                    null)
            ])),
        };

        var journal = new FakeOrderReferenceJournal();
        await journal.SaveAsync(new OrderSubmissionRecord(
            "CLOSESUBMITTED1",
            OrderSubmissionKind.Close,
            DateTimeOffset.UtcNow,
            new InstrumentId("CC.D.VIX.UMA.IP"),
            TradeDirection.Sell,
            2m,
            "OPENDEAL1"));

        var gateway = CreateGateway(api, journal);

        var status = await gateway.GetOrderStatusAsync("CLOSESUBMITTED1");

        status.Should().NotBeNull();
        status!.Status.Should().Be(OrderStatus.Closed);
        status.DealReference.Should().Be("CLOSESUBMITTED1");
        status.DealId.Should().Be("DIAAAAWBROKER1");
    }

    [Fact]
    public async Task GetWorkingOrdersAsync_ShouldMapWorkingOrders()
    {
        var api = new FakeIgTradingApi
        {
            WorkingOrders = _ => Task.FromResult(new WorkingOrdersResponse(
            [
                new WorkingOrderEnvelope(
                    new WorkingOrderData(
                        "WO1",
                        "BUY",
                        "CC.D.VIX.UMA.IP",
                        1m,
                        10m,
                        "GOOD_TILL_CANCELLED",
                        null,
                        null,
                        DateTimeOffset.UtcNow.ToString("O"),
                        false,
                        "LIMIT",
                        null,
                        null,
                        "AUD"),
                    new WorkingOrderMarketData("Volatility Index", "CC.D.VIX.UMA.IP", "-", "TRADEABLE"))
            ])),
        };

        var gateway = CreateGateway(api);

        var orders = await gateway.GetWorkingOrdersAsync();

        orders.Should().ContainSingle();
        orders[0].DealId.Should().Be("WO1");
        orders[0].Type.Should().Be(WorkingOrderType.Limit);
    }

    [Fact]
    public async Task PlaceWorkingOrderAsync_ShouldSubmitExpectedPayload()
    {
        Ig.Trading.Sdk.Models.CreateWorkingOrderRequest? capturedRequest = null;

        var api = new FakeIgTradingApi
        {
            CreateWorkingOrder = request =>
            {
                capturedRequest = request;
                return Task.FromResult(new WorkingOrderMutationResponse("WOREF1"));
            },
        };

        var gateway = CreateGateway(api);

        var result = await gateway.PlaceWorkingOrderAsync(new CreateWorkingOrderRequest(
            new InstrumentId("CC.D.VIX.UMA.IP"),
            TradeDirection.Buy,
            WorkingOrderType.Limit,
            1m,
            10m,
            WorkingOrderTimeInForce.GoodTillCancelled));

        result.DealReference.Should().Be("WOREF1");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Type.Should().Be("LIMIT");
        capturedRequest.TimeInForce.Should().Be("GOOD_TILL_CANCELLED");
    }

    [Fact]
    public async Task UpdateWorkingOrderAsync_ShouldSubmitExpectedPayload()
    {
        string? capturedDealId = null;
        Ig.Trading.Sdk.Models.UpdateWorkingOrderRequest? capturedRequest = null;

        var api = new FakeIgTradingApi
        {
            WorkingOrders = _ => Task.FromResult(new WorkingOrdersResponse(
            [
                new WorkingOrderEnvelope(
                    new WorkingOrderData(
                        "WO1",
                        "BUY",
                        "CC.D.VIX.UMA.IP",
                        1m,
                        10m,
                        "GOOD_TILL_CANCELLED",
                        null,
                        null,
                        DateTimeOffset.UtcNow.ToString("O"),
                        false,
                        "LIMIT",
                        null,
                        null,
                        "AUD"),
                    new WorkingOrderMarketData("Volatility Index", "CC.D.VIX.UMA.IP", "-", "TRADEABLE"))
            ])),
            UpdateWorkingOrder = (dealId, request) =>
            {
                capturedDealId = dealId;
                capturedRequest = request;
                return Task.FromResult(new WorkingOrderMutationResponse("WOUPD1"));
            },
        };

        var gateway = CreateGateway(api);

        var result = await gateway.UpdateWorkingOrderAsync(new UpdateWorkingOrderRequest("WO1", 11m, WorkingOrderType.Limit, WorkingOrderTimeInForce.GoodTillCancelled, null));

        result.DealReference.Should().Be("WOUPD1");
        result.DealId.Should().Be("WO1");
        capturedDealId.Should().Be("WO1");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Level.Should().Be(11m);
        capturedRequest.Type.Should().Be("LIMIT");
        capturedRequest.TimeInForce.Should().Be("GOOD_TILL_CANCELLED");
    }

    [Fact]
    public async Task CancelWorkingOrderAsync_ShouldReturnAcceptedResult()
    {
        var api = new FakeIgTradingApi
        {
            DeleteWorkingOrder = _ => Task.FromResult(new WorkingOrderMutationResponse("WOCANCEL1")),
        };

        var gateway = CreateGateway(api);

        var result = await gateway.CancelWorkingOrderAsync("WO1");

        result.DealReference.Should().Be("WOCANCEL1");
        result.DealId.Should().Be("WO1");
        result.Status.Should().Be(OrderStatus.Accepted);
    }

    private static IgTradingGateway CreateGateway(IIgTradingApi api, IOrderReferenceJournal? journal = null)
        => new(api, journal ?? new NullOrderReferenceJournal(), NullLogger<IgTradingGateway>.Instance);

    private sealed class FakeIgTradingApi : IIgTradingApi
    {
        public Func<CancellationToken, Task<IgSessionContext>> Authenticate { get; set; }
            = _ => Task.FromResult(new IgSessionContext("cst", "token", "ACC1", DateTimeOffset.UtcNow));

        public Func<string, Task<MarketDetailsResponse>> Market { get; set; }
            = _ => Task.FromResult(new MarketDetailsResponse(new MarketInstrument("IX.D.SPTRD.DAILY.IP", "DFB", [new MarketCurrency("USD", true)]), new MarketSnapshot("TRADEABLE")));

        public Func<CreatePositionRequest, Task<CreatePositionResponse>> CreatePosition { get; set; }
            = _ => Task.FromResult(new CreatePositionResponse("REF-NEW"));

        public Func<Ig.Trading.Sdk.Models.CreateWorkingOrderRequest, Task<WorkingOrderMutationResponse>> CreateWorkingOrder { get; set; }
            = _ => Task.FromResult(new WorkingOrderMutationResponse("WOREF"));

        public Func<string, Ig.Trading.Sdk.Models.UpdateWorkingOrderRequest, Task<WorkingOrderMutationResponse>> UpdateWorkingOrder { get; set; }
            = (_, _) => Task.FromResult(new WorkingOrderMutationResponse("WOUPD"));

        public Func<string, Task<WorkingOrderMutationResponse>> DeleteWorkingOrder { get; set; }
            = _ => Task.FromResult(new WorkingOrderMutationResponse("WODEL"));

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

        public Func<CancellationToken, Task<TransactionHistoryResponse>> Transactions { get; set; }
            = _ => Task.FromResult(new TransactionHistoryResponse([], new TransactionMetadata(0, null)));

        public Func<string, Task<PositionEnvelope?>> PositionByDealId { get; set; }
            = _ => Task.FromResult<PositionEnvelope?>(null);

        public Func<CancellationToken, Task<AccountsResponse>> Accounts { get; set; }
            = _ => Task.FromResult(new AccountsResponse([]));

        public Task<IgSessionContext> AuthenticateAsync(CancellationToken cancellationToken = default) => Authenticate(cancellationToken);

        public Task<MarketDetailsResponse> GetMarketByEpicAsync(string epic, CancellationToken cancellationToken = default) => Market(epic);

        public Task<CreatePositionResponse> CreatePositionAsync(CreatePositionRequest request, CancellationToken cancellationToken = default) => CreatePosition(request);

        public Task<WorkingOrderMutationResponse> CreateWorkingOrderAsync(Ig.Trading.Sdk.Models.CreateWorkingOrderRequest request, CancellationToken cancellationToken = default) => CreateWorkingOrder(request);

        public Task<WorkingOrderMutationResponse> UpdateWorkingOrderAsync(string dealId, Ig.Trading.Sdk.Models.UpdateWorkingOrderRequest request, CancellationToken cancellationToken = default) => UpdateWorkingOrder(dealId, request);

        public Task<WorkingOrderMutationResponse> DeleteWorkingOrderAsync(string dealId, CancellationToken cancellationToken = default) => DeleteWorkingOrder(dealId);

        public Task<ClosePositionResponse> ClosePositionAsync(Ig.Trading.Sdk.Models.ClosePositionRequest request, CancellationToken cancellationToken = default) => ClosePosition(request);

        public Task<PositionsResponse> GetOpenPositionsAsync(CancellationToken cancellationToken = default) => OpenPositions(cancellationToken);

        public Task<PositionEnvelope?> GetPositionByDealIdAsync(string dealId, CancellationToken cancellationToken = default) => PositionByDealId(dealId);

        public Task<DealConfirmationResponse?> GetDealConfirmationAsync(string dealReference, CancellationToken cancellationToken = default) => DealConfirmation(dealReference);

        public Task<WorkingOrdersResponse> GetWorkingOrdersAsync(CancellationToken cancellationToken = default) => WorkingOrders(cancellationToken);

        public Task<ActivityResponse> GetActivityAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, int pageSize, CancellationToken cancellationToken = default)
            => Activity(fromUtc, toUtc, pageSize);

        public Task<TransactionHistoryResponse> GetTransactionsAsync(CancellationToken cancellationToken = default) => Transactions(cancellationToken);

        public Task<AccountsResponse> GetAccountsAsync(CancellationToken cancellationToken = default) => Accounts(cancellationToken);
    }

    private sealed class FakeOrderReferenceJournal : IOrderReferenceJournal
    {
        private readonly Dictionary<string, OrderSubmissionRecord> _records = [];

        public Task SaveAsync(OrderSubmissionRecord record, CancellationToken cancellationToken = default)
        {
            _records[record.DealReference] = record;
            return Task.CompletedTask;
        }

        public Task<OrderSubmissionRecord?> GetAsync(string dealReference, CancellationToken cancellationToken = default)
            => Task.FromResult(_records.GetValueOrDefault(dealReference));
    }
}
