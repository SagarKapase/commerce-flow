namespace Basket.Api.Exceptions;

/// <summary>
/// Thrown when Catalog says the requested product cannot be bought - either it
/// does not exist, or it has been deactivated. Maps to 400 Bad Request.
///
/// WHY 400 AND NOT 404: 404 describes the resource named in the URL, and
/// POST /api/basket/items exists. The problem is the productId inside the body,
/// which makes the body invalid. Same reasoning as Catalog's unknown-category
/// case - and being consistent about it across services is what makes an API
/// predictable.
/// </summary>
public sealed class ProductNotAvailableException : BasketApplicationException
{
    public ProductNotAvailableException(Guid productId, string reason)
        : base($"Product '{productId}' cannot be added to a basket because {reason}.")
    {
        ProductId = productId;
    }

    public Guid ProductId { get; }
}
