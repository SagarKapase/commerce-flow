using System.ComponentModel.DataAnnotations;
using Basket.Api.Domain;

namespace Basket.Api.Dtos;

/// <summary>
/// The body of POST /api/basket/items.
///
/// ================= WHAT PHASE 5 REMOVED =================
/// This DTO used to carry productName and unitPrice, supplied by the client.
/// That meant a caller could send
///     { "productId": "...", "productName": "Free stuff", "unitPrice": 0.01 }
/// and check out a laptop for one paisa. Both fields are now gone.
///
/// Name and price come from Catalog, the service that OWNS them. What is left
/// here is exactly what a client is entitled to decide: WHICH product, and HOW
/// MANY. Everything else is a server-side fact.
///
/// The general rule, worth stating in an interview in one sentence: never
/// accept from a client any value the client could profit from changing.
/// Prices, roles, user ids, discounts and totals are always looked up, never
/// received.
///
/// Notice also that the fix was not "validate the price the client sent" -
/// there is nothing to validate it against except the real price, and once you
/// have fetched the real price you have no use for theirs. Removing a field
/// beats checking it.
/// =========================================================
/// </summary>
public sealed class AddBasketItemRequest
{
    /// <summary>
    /// The product to add. Owned by the Catalog service, which will be asked
    /// whether it exists and is still on sale before this request succeeds.
    /// </summary>
    public Guid ProductId { get; init; }

    /// <summary>How many to add to the line.</summary>
    [Range(1, BasketItem.MaxQuantity)]
    public int Quantity { get; init; } = 1;
}
