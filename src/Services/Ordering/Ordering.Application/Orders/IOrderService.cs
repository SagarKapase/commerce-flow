using Ordering.Application.Common;
using Ordering.Application.Orders.Dtos;

namespace Ordering.Application.Orders;

/// <summary>
/// Order use cases.
///
/// Note that every method that touches a specific order takes BOTH the order id
/// and the caller's identity. That is not redundancy - it is how ownership is
/// enforced. There is no "get order 5" without also saying who is asking, so
/// there is no code path that can forget to check.
/// </summary>
public interface IOrderService
{
    Task<OrderResponse> PlaceAsync(
        Guid customerId,
        CreateOrderRequest request,
        CancellationToken cancellationToken);

    /// <returns>
    /// The order, or <c>null</c> if it does not exist OR does not belong to
    /// this caller. The two cases are deliberately indistinguishable - see
    /// OrderService.
    /// </returns>
    Task<OrderResponse?> GetForCustomerAsync(
        Guid orderId,
        Guid customerId,
        bool callerIsAdmin,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderResponse>> GetMyOrdersAsync(
        Guid customerId,
        CancellationToken cancellationToken);

    /// <summary>Administrators only - every customer's orders.</summary>
    Task<PagedResponse<OrderResponse>> GetPageAsync(
        OrderListQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Pays for an order whose stock is already reserved, then either confirms
    /// the reservation or releases it.
    /// </summary>
    /// <returns><c>null</c> if the order does not exist or is not the caller's.</returns>
    Task<OrderResponse?> PayAsync(
        Guid orderId,
        Guid customerId,
        bool callerIsAdmin,
        PayOrderRequest request,
        CancellationToken cancellationToken);

    /// <returns><c>null</c> if the order does not exist or is not the caller's.</returns>
    Task<OrderResponse?> CancelAsync(
        Guid orderId,
        Guid customerId,
        bool callerIsAdmin,
        CancellationToken cancellationToken);
}
