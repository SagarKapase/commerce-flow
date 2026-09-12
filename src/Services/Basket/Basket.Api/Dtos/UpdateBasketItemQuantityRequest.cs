using System.ComponentModel.DataAnnotations;
using Basket.Api.Domain;

namespace Basket.Api.Dtos;

/// <summary>
/// The body of PUT /api/basket/items/{productId}.
///
/// It carries only the quantity. The product comes from the route and the user
/// comes from the token, so the only thing left for the body to say is "how
/// many" - and a request object with exactly one meaningful field is a good
/// sign the endpoint is doing exactly one thing.
/// </summary>
public sealed class UpdateBasketItemQuantityRequest
{
    /// <summary>
    /// Minimum 1, not 0. "Set the quantity to zero" is a removal, and removal
    /// already has a verb: DELETE. Two ways to do one thing is two code paths
    /// to keep correct.
    /// </summary>
    [Range(1, BasketItem.MaxQuantity)]
    public int Quantity { get; init; }
}
