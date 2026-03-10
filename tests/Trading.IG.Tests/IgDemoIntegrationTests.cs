using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Trading.Abstractions;

namespace Trading.IG.Tests;

public class IgDemoIntegrationTests
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task AuthenticateAsync_WithValidDemoCredentials_ShouldReturnSession()
    {
        await using var context = await IgDemoIntegrationContext.CreateAsync();
        var session = await context.AuthenticateAsync();

        session.BrokerName.Should().Be("IG");
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task FullDemoRun_WithValidDemoCredentials_ShouldExerciseImplementedEndpoints()
    {
        await using var context = await IgDemoIntegrationContext.CreateAsync();
        var session = await context.AuthenticateAsync();
        var updatedLevel = context.WorkingOrderLevel + 1m;
        var baselinePositions = await context.Gateway.GetOpenPositionsAsync();

        session.BrokerName.Should().Be("IG");

        var accounts = await context.IgTradingApi.GetAccountsAsync();
        (accounts.Accounts ?? []).Should().NotBeEmpty();

        var createdWorkingOrder = await context.Gateway.PlaceWorkingOrderAsync(new CreateWorkingOrderRequest(
            new InstrumentId(context.Epic),
            TradeDirection.Buy,
            WorkingOrderType.Limit,
            context.Size,
            context.WorkingOrderLevel,
            WorkingOrderTimeInForce.GoodTillCancelled));

        createdWorkingOrder.Status.Should().Be(OrderStatus.Accepted);

        var createdWorkingOrderStatus = await context.Gateway.GetOrderStatusAsync(createdWorkingOrder.DealReference);
        context.WorkingOrderDealId = createdWorkingOrderStatus?.DealId;
        context.WorkingOrderDealId.Should().NotBeNullOrWhiteSpace();

        var workingOrders = await context.Gateway.GetWorkingOrdersAsync();
        workingOrders.Should().Contain(order => order.DealId == context.WorkingOrderDealId);

        var updatedWorkingOrder = await context.Gateway.UpdateWorkingOrderAsync(new UpdateWorkingOrderRequest(
            context.WorkingOrderDealId!,
            updatedLevel,
            WorkingOrderType.Limit,
            WorkingOrderTimeInForce.GoodTillCancelled,
            null));

        updatedWorkingOrder.Status.Should().Be(OrderStatus.Accepted);

        var workingOrdersAfterUpdate = await context.Gateway.GetWorkingOrdersAsync();
        workingOrdersAfterUpdate.Should().Contain(order =>
            order.DealId == context.WorkingOrderDealId &&
            order.Level == updatedLevel);

        var cancelledWorkingOrder = await context.Gateway.CancelWorkingOrderAsync(context.WorkingOrderDealId!);
        cancelledWorkingOrder.Status.Should().Be(OrderStatus.Accepted);

        var cancelledWorkingOrderStatus = await context.Gateway.GetOrderStatusAsync(cancelledWorkingOrder.DealReference);
        cancelledWorkingOrderStatus.Should().NotBeNull();
        cancelledWorkingOrderStatus.Status.Should().Be(OrderStatus.Closed);
        context.WorkingOrderDealId = null;

        var marketOrder = await context.Gateway.PlaceMarketOrderAsync(new PlaceOrderRequest(
            new InstrumentId(context.Epic),
            TradeDirection.Buy,
            context.Size));

        marketOrder.Status.Should().NotBe(OrderStatus.Rejected);

        var openedOrderStatus = await context.Gateway.GetOrderStatusAsync(marketOrder.DealReference);
        context.PositionDealId = openedOrderStatus?.DealId;

        if (string.IsNullOrWhiteSpace(context.PositionDealId))
        {
            var positionsAfterOpen = await context.WaitForPositionCountChangeAsync(
                baselinePositions.Count,
                expectedMinimumCount: baselinePositions.Count + 1,
                timeout: TimeSpan.FromSeconds(20));

            context.PositionDealId = positionsAfterOpen
                .FirstOrDefault(position => baselinePositions.All(existing => !string.Equals(existing.DealId, position.DealId, StringComparison.OrdinalIgnoreCase)))
                ?.DealId;
        }

        context.PositionDealId.Should().NotBeNullOrWhiteSpace();
        await context.WaitForPositionPresenceAsync(context.PositionDealId!, shouldExist: true, TimeSpan.FromSeconds(20));

        var positions = await context.Gateway.GetOpenPositionsAsync();
        positions.Should().Contain(position => position.DealId == context.PositionDealId);

        var positionByDealId = await context.IgTradingApi.GetPositionByDealIdAsync(context.PositionDealId!);
        positionByDealId.Should().NotBeNull();
        positionByDealId!.Position.DealId.Should().Be(context.PositionDealId);

        var orders = await context.Gateway.GetOrdersAsync(new OrderQuery(
            DateTimeOffset.UtcNow.AddHours(-24),
            DateTimeOffset.UtcNow,
            100));

        orders.Should().NotBeEmpty();

        var transactions = await context.IgTradingApi.GetTransactionsAsync();
        transactions.Should().NotBeNull();

        var closedPosition = await context.Gateway.ClosePositionAsync(new ClosePositionRequest(context.PositionDealId!, null));
        closedPosition.Status.Should().NotBe(OrderStatus.Rejected);

        var closedPositionStatus = await context.Gateway.GetOrderStatusAsync(closedPosition.DealReference);
        closedPositionStatus.Should().NotBeNull();
        closedPositionStatus.Status.Should().BeOneOf(OrderStatus.Pending, OrderStatus.Closed);

        await context.WaitForPositionPresenceAsync(context.PositionDealId!, shouldExist: false, TimeSpan.FromSeconds(30));
        context.PositionDealId = null;

        var finalPositions = await context.Gateway.GetOpenPositionsAsync();
        finalPositions.Should().NotContain(position => string.Equals(position.DealId, closedPositionStatus.DealId, StringComparison.OrdinalIgnoreCase));
    }
}
