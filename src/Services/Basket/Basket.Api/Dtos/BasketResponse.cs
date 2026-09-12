namespace Basket.Api.Dtos;

/// <summary>
/// The whole basket, as the API describes it.
///
/// TotalQuantity and TotalAmount are computed by the aggregate and sent here so
/// the client never adds prices up itself. Two clients doing that arithmetic
/// independently is two chances to disagree with the server about what the
/// customer owes.
/// </summary>
public sealed record BasketResponse(
    Guid UserId,
    IReadOnlyList<BasketItemResponse> Items,
    int TotalQuantity,
    decimal TotalAmount,
    DateTime? UpdatedAtUtc);

/// <summary>One line of the basket.</summary>
public sealed record BasketItemResponse(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);
