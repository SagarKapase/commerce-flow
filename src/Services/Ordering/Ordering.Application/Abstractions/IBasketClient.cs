namespace Ordering.Application.Abstractions;

/// <summary>
/// One line of the customer's basket, as ORDERING understands it.
///
/// Basket's real response carries userId, totalQuantity, totalAmount,
/// updatedAtUtc and a lineTotal per item. Ordering declares the four fields it
/// needs and lets System.Text.Json discard the rest - a tolerant reader, the
/// same contract discipline Basket uses when it reads Catalog.
///
/// The price here is the one BASKET recorded, which Basket itself fetched from
/// Catalog in Phase 5. So by the time it reaches an order it has been
/// server-derived twice and never touched by the client.
/// </summary>
public sealed record BasketLineSnapshot(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity);

/// <summary>Ordering's view of the Basket service.</summary>
public interface IBasketClient
{
    /// <summary>
    /// Reads the CALLING CUSTOMER's basket.
    ///
    /// Note there is no userId parameter, and that is not an oversight. Basket
    /// derives the owner from the "sub" claim of the token on the request, so
    /// Ordering forwards the caller's token rather than naming a user. There is
    /// no way for this method to ask for somebody else's basket, which is the
    /// same protection Basket's own controller has - carried across a service
    /// boundary instead of lost at it.
    /// </summary>
    /// <returns>The lines, possibly empty.</returns>
    /// <exception cref="Exceptions.DownstreamUnavailableException">Basket could not be reached.</exception>
    Task<IReadOnlyList<BasketLineSnapshot>> GetCurrentBasketAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Empties the calling customer's basket after their order is placed.
    ///
    /// Best-effort by design - see OrderService for why a failure here must not
    /// fail the order.
    /// </summary>
    Task ClearCurrentBasketAsync(CancellationToken cancellationToken);
}
