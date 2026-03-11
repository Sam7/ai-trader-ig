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
                new MarketInstrument("IX.D.SPTRD.DAILY.IP", null, null, "DFB", [new MarketCurrency("USD", true)]),
                new MarketSnapshot("CLOSED", null, null),
                null)),
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
    public async Task UpdatePositionAsync_ShouldSubmitExpectedPayload()
    {
        string? capturedDealId = null;
        Ig.Trading.Sdk.Models.UpdatePositionRequest? capturedRequest = null;

        var api = new FakeIgTradingApi
        {
            PositionByDealId = _ => Task.FromResult<PositionEnvelope?>(new PositionEnvelope(
                new PositionData("P1", "BUY", 1m, "USD", 12m, DateTimeOffset.UtcNow.ToString("O"), 15m, 10m, null, null),
                new PositionMarketData("IX.D.SPTRD.DAILY.IP", "DFB"))),
            UpdatePosition = (dealId, request) =>
            {
                capturedDealId = dealId;
                capturedRequest = request;
                return Task.FromResult(new UpdatePositionResponse("AMENDREF1"));
            },
            DealConfirmation = _ => Task.FromResult<DealConfirmationResponse?>(new DealConfirmationResponse(
                "ACCEPTED",
                "OPEN",
                null,
                "P1",
                "AMENDREF1",
                "IX.D.SPTRD.DAILY.IP",
                "BUY",
                1m,
                DateTimeOffset.UtcNow.ToString("O"))),
        };

        var gateway = CreateGateway(api);

        var result = await gateway.UpdatePositionAsync(new Trading.Abstractions.UpdatePositionRequest("P1", 11m, 16m, null, null));

        result.DealReference.Should().Be("AMENDREF1");
        capturedDealId.Should().Be("P1");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.StopLevel.Should().Be(11m);
        capturedRequest.LimitLevel.Should().Be(16m);
        capturedRequest.TrailingStop.Should().BeFalse();
    }

    [Fact]
    public async Task SearchMarketsAsync_ShouldMapAndLimitResults()
    {
        var api = new FakeIgTradingApi
        {
            SearchMarkets = _ => Task.FromResult(new MarketSearchResponse(
            [
                new MarketSearchItem("Volatility Index", "CC.D.VIX.UMA.IP", "-", "INDICES", "TRADEABLE", "USD"),
                new MarketSearchItem("US 500", "IX.D.SPTRD.DAILY.IP", "DFB", "INDICES", "CLOSED", "USD"),
            ])),
        };

        var gateway = CreateGateway(api);

        var results = await gateway.SearchMarketsAsync("VIX", maxResults: 1);

        results.Should().ContainSingle();
        results[0].Instrument.Value.Should().Be("CC.D.VIX.UMA.IP");
        results[0].Status.Should().Be(MarketStatus.Tradeable);
    }

    [Fact]
    public async Task BrowseMarketsAsync_ShouldMapNodesAndMarkets()
    {
        var api = new FakeIgTradingApi
        {
            MarketNavigation = _ => Task.FromResult(new MarketNavigationResponse(
                "Indices",
                [new MarketNavigationNodeItem("1", "Major indices")],
                [new MarketSearchItem("US 500", "IX.D.SPTRD.DAILY.IP", "DFB", "INDICES", "TRADEABLE", "USD")])),
        };

        var gateway = CreateGateway(api);

        var page = await gateway.BrowseMarketsAsync("root");

        page.Name.Should().Be("Indices");
        page.Nodes.Should().ContainSingle(node => node.Id == "1");
        page.Markets.Should().ContainSingle(market => market.Instrument.Value == "IX.D.SPTRD.DAILY.IP");
    }

    [Fact]
    public async Task GetPricesAsync_ShouldMapBars()
    {
        var api = new FakeIgTradingApi
        {
            Prices = _ => Task.FromResult(new PricesResponse(
            [
                new PricePoint(
                    DateTimeOffset.UtcNow.ToString("O"),
                    new PriceLevel(10m, 11m),
                    new PriceLevel(12m, 13m),
                    new PriceLevel(9m, 10m),
                    new PriceLevel(11m, 12m),
                    42)
                {
                    TimestampUtc = DateTimeOffset.Parse("2026-03-11T00:00:00Z"),
                }
            ],
            "INDICES",
            null)),
        };

        var gateway = CreateGateway(api);

        var series = await gateway.GetPricesAsync(new Trading.Abstractions.GetPricesRequest(
            new InstrumentId("CC.D.VIX.UMA.IP"),
            PriceResolution.Minute,
            1));

        series.Bars.Should().ContainSingle();
        series.Bars[0].TimestampUtc.Should().Be(DateTimeOffset.Parse("2026-03-11T00:00:00Z"));
        series.Bars[0].BidOpen.Should().Be(10m);
        series.Bars[0].AskClose.Should().Be(12m);
    }

    [Fact]
    public async Task GetWorkingOrdersAsync_WithInvalidBrokerDate_ShouldThrowBrokerError()
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
                        "not-a-date",
                        false,
                        "LIMIT",
                        null,
                        null,
                        "AUD"),
                    new WorkingOrderMarketData("Volatility Index", "CC.D.VIX.UMA.IP", "-", "TRADEABLE"))
            ])),
        };

        var gateway = CreateGateway(api);

        var action = () => gateway.GetWorkingOrdersAsync();

        var exception = await action.Should().ThrowAsync<TradingGatewayException>();
        exception.Which.ErrorCode.Should().Be(TradingErrorCode.BrokerError);
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
            = _ => Task.FromResult(new MarketDetailsResponse(new MarketInstrument("IX.D.SPTRD.DAILY.IP", null, null, "DFB", [new MarketCurrency("USD", true)]), new MarketSnapshot("TRADEABLE", 100m, 101m), new MarketDealingRules(new MarketRuleDistance(1m, "POINTS"))));

        public Func<string, Task<MarketSearchResponse>> SearchMarkets { get; set; }
            = _ => Task.FromResult(new MarketSearchResponse([]));

        public Func<string?, Task<MarketNavigationResponse>> MarketNavigation { get; set; }
            = _ => Task.FromResult(new MarketNavigationResponse("Markets", [], []));

        public Func<Ig.Trading.Sdk.Models.GetPricesRequest, Task<PricesResponse>> Prices { get; set; }
            = _ => Task.FromResult(new PricesResponse([], null, null));

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

        public Func<string, Ig.Trading.Sdk.Models.UpdatePositionRequest, Task<UpdatePositionResponse>> UpdatePosition { get; set; }
            = (_, _) => Task.FromResult(new UpdatePositionResponse("REF-AMEND"));

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

        public Task<MarketSearchResponse> SearchMarketsAsync(string searchTerm, CancellationToken cancellationToken = default) => SearchMarkets(searchTerm);

        public Task<MarketNavigationResponse> GetMarketNavigationAsync(string? nodeId = null, CancellationToken cancellationToken = default) => MarketNavigation(nodeId);

        public Task<PricesResponse> GetPricesAsync(Ig.Trading.Sdk.Models.GetPricesRequest request, CancellationToken cancellationToken = default) => Prices(request);

        public Task<CreatePositionResponse> CreatePositionAsync(CreatePositionRequest request, CancellationToken cancellationToken = default) => CreatePosition(request);

        public Task<WorkingOrderMutationResponse> CreateWorkingOrderAsync(Ig.Trading.Sdk.Models.CreateWorkingOrderRequest request, CancellationToken cancellationToken = default) => CreateWorkingOrder(request);

        public Task<WorkingOrderMutationResponse> UpdateWorkingOrderAsync(string dealId, Ig.Trading.Sdk.Models.UpdateWorkingOrderRequest request, CancellationToken cancellationToken = default) => UpdateWorkingOrder(dealId, request);

        public Task<WorkingOrderMutationResponse> DeleteWorkingOrderAsync(string dealId, CancellationToken cancellationToken = default) => DeleteWorkingOrder(dealId);

        public Task<ClosePositionResponse> ClosePositionAsync(Ig.Trading.Sdk.Models.ClosePositionRequest request, CancellationToken cancellationToken = default) => ClosePosition(request);

        public Task<UpdatePositionResponse> UpdatePositionAsync(string dealId, Ig.Trading.Sdk.Models.UpdatePositionRequest request, CancellationToken cancellationToken = default) => UpdatePosition(dealId, request);

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
