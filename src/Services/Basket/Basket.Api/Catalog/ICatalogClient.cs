namespace Basket.Api.Catalog;

/// <summary>
/// Basket's view of the Catalog service.
///
/// The interface exists so BasketService can be reasoned about - and later
/// tested - without a live Catalog on the other end. Note it says nothing about
/// HTTP: no URLs, no status codes, no HttpClient. The application code asks
/// "what is this product?" and the answer is either a product, nothing, or an
/// exception saying the question could not be asked.
/// </summary>
public interface ICatalogClient
{
    /// <summary>
    /// Looks up one product.
    /// </summary>
    /// <returns>
    /// The product, or <c>null</c> if Catalog says there is no such product.
    /// </returns>
    /// <exception cref="Exceptions.CatalogUnavailableException">
    /// Catalog could not be reached, timed out, or answered with an error.
    ///
    /// THIS DISTINCTION IS THE WHOLE POINT. "There is no such product" is an
    /// ANSWER - null - and the caller's request was wrong. "I could not ask"
    /// is not an answer at all, and the caller's request may have been perfect.
    /// Collapsing both into null would tell a customer their product does not
    /// exist because a server in another process happened to be restarting.
    /// </exception>
    Task<CatalogProduct?> GetProductAsync(Guid productId, CancellationToken cancellationToken);
}
