namespace Inventory.Application.Exceptions;

/// <summary>
/// Thrown when a reservation names a product Inventory has never heard of.
/// Maps to 400 Bad Request.
///
/// "Never heard of" here means "has no stock record", which is not the same as
/// "does not exist in Catalog". A brand new product exists and is on sale
/// before anybody receives the first delivery of it. Inventory only knows about
/// products somebody has adjusted stock for, and that is the honest answer to
/// give: we cannot reserve what we have never counted.
/// </summary>
public sealed class UnknownProductException : InventoryApplicationException
{
    public UnknownProductException(Guid productId)
        : base($"No stock record exists for product '{productId}'.")
    {
        ProductId = productId;
    }

    public Guid ProductId { get; }
}
