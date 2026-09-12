using System.ComponentModel.DataAnnotations;
using Ordering.Domain.Entities;
using Ordering.Domain.ValueObjects;

namespace Ordering.Application.Orders.Dtos;

/// <summary>
/// The body of POST /api/orders.
///
/// ================= WHAT PHASE 8 REMOVED =================
/// This used to carry the whole item list, prices included. It now carries a
/// shipping address and nothing else: the items come from the caller's basket,
/// fetched over HTTP with their own token.
///
/// That is a DIFFERENT fix from the one Basket got in Phase 5, and the
/// difference is the lesson:
///
///   Phase 5 fix:  do not TRUST the client's value - fetch the real one from
///                 the service that owns it.
///   Phase 8 fix:  do not ASK the client at all - derive it from state we
///                 already hold on our own side of the wire.
///
/// The second is strictly stronger. A lookup can still be handed a product the
/// customer never chose, at a quantity they never picked; a basket cannot,
/// because the basket IS the record of what they chose. Removing the input
/// beats validating it, every time it is possible.
///
/// Notice also what vanished with it: the IValidatableObject that rejected
/// duplicate products. Basket's items are keyed on (UserId, ProductId), so a
/// duplicate is unrepresentable upstream and there is nothing left to check.
/// Validation that guards an input you no longer accept is dead code, and dead
/// code that looks like a security control is worse than none.
/// ========================================================
/// </summary>
public sealed class CreateOrderRequest
{
    /// <summary>
    /// Where to send it. The ONLY thing left in this request.
    /// </summary>
    [Required]
    public AddressRequest ShippingAddress { get; init; } = new();
}

public sealed class AddressRequest
{
    [Required]
    [StringLength(Address.Line1MaxLength, MinimumLength = 3)]
    public string Line1 { get; init; } = string.Empty;

    [Required]
    [StringLength(Address.CityMaxLength, MinimumLength = 2)]
    public string City { get; init; } = string.Empty;

    [Required]
    [StringLength(Address.PostalCodeMaxLength, MinimumLength = 3)]
    public string PostalCode { get; init; } = string.Empty;

    [Required]
    [StringLength(Address.CountryMaxLength, MinimumLength = 2)]
    public string Country { get; init; } = string.Empty;
}

/// <summary>
/// An order, as the API describes it.
///
/// Status is a STRING, not the enum's number. A client reading
/// "status": "InventoryReserved" needs no lookup table, and inserting a value
/// into the middle of the enum later cannot silently change what 4 means to
/// everybody who already shipped code against it.
///
/// CanBeCancelled is included so a UI can disable the button instead of
/// offering it and collecting a 409. The server still enforces the rule - this
/// is a convenience, never the check.
/// </summary>
public sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    string Status,
    IReadOnlyList<OrderItemResponse> Items,
    decimal TotalAmount,
    AddressResponse ShippingAddress,
    bool CanBeCancelled,
    Guid? InventoryReservationId,
    Guid? PaymentId,
    string? FailureReason,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record OrderItemResponse(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public sealed record AddressResponse(
    string Line1,
    string City,
    string PostalCode,
    string Country);

/// <summary>Query parameters for the administrator's order list.</summary>
public sealed class OrderListQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Optional filter. null returns every status.</summary>
    public OrderStatus? Status { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, MaxPageSize)]
    public int PageSize { get; init; } = DefaultPageSize;
}

/// <summary>
/// The body of POST /api/orders/{id}/cancel's sibling, POST /api/orders/{id}/pay.
///
/// One field, and it is the only payment detail a customer ever sends us: a
/// TOKEN standing in for their card, minted by the provider's client-side SDK.
///
/// Note what is NOT here: an amount. The customer does not get to say what
/// their order costs - that comes from the order, which came from the basket,
/// which came from Catalog. Nor a card number: tokenisation exists so card
/// data never touches our servers, and therefore cannot leak from them.
/// </summary>
public sealed class PayOrderRequest
{
    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string PaymentMethodToken { get; init; } = string.Empty;
}
