using Ordering.Application.Orders.Dtos;
using Ordering.Domain.Entities;

namespace Ordering.Application.Orders;

internal static class OrderMappings
{
    public static OrderResponse ToResponse(Order order)
    {
        return new OrderResponse(
            order.Id,
            order.CustomerId,

            // .ToString() on the enum, so the wire carries "Confirmed" rather
            // than 4. See OrderResponse for why that matters to a client.
            order.Status.ToString(),

            order.Items
                .OrderBy(item => item.ProductName)
                .Select(item => new OrderItemResponse(
                    item.ProductId,
                    item.ProductName,
                    item.UnitPrice,
                    item.Quantity,
                    item.LineTotal))
                .ToList(),
            order.TotalAmount,
            new AddressResponse(
                order.ShippingAddress.Line1,
                order.ShippingAddress.City,
                order.ShippingAddress.PostalCode,
                order.ShippingAddress.Country),
            order.CanBeCancelled,
            order.InventoryReservationId,
            order.PaymentId,
            order.FailureReason,
            order.CreatedAtUtc,
            order.UpdatedAtUtc);
    }
}
