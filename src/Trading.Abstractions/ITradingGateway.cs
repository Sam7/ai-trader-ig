namespace Trading.Abstractions;

public interface ITradingGateway
{
    Task<ITradingSession> AuthenticateAsync(CancellationToken cancellationToken = default);

    Task<PlaceOrderResult> PlaceMarketOrderAsync(
        PlaceOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<ClosePositionResult> ClosePositionAsync(
        ClosePositionRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PositionSummary>> GetOpenPositionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderSummary>> GetOrdersAsync(
        OrderQuery query,
        CancellationToken cancellationToken = default);

    Task<OrderSummary?> GetOrderStatusAsync(
        string dealReference,
        CancellationToken cancellationToken = default);
}
