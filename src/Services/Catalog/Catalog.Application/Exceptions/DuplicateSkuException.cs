namespace Catalog.Application.Exceptions;

/// <summary>
/// Thrown when a product would be created or renamed onto a SKU that already
/// exists. Maps to 409 Conflict: the request is valid, but it conflicts with
/// the current state of the resource.
/// </summary>
public sealed class DuplicateSkuException : CatalogApplicationException
{
    public DuplicateSkuException(string sku)
        : base($"A product with SKU '{sku}' already exists.")
    {
        Sku = sku;
    }

    public string Sku { get; }
}
