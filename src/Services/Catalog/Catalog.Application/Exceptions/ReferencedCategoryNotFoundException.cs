namespace Catalog.Application.Exceptions;

/// <summary>
/// Thrown when a product request points at a category that does not exist.
///
/// This maps to 400 Bad Request, not 404 Not Found. 404 describes the resource
/// named in the URL - and POST /api/products does exist. The problem is the
/// categoryId *inside the body*, which makes the body invalid.
/// </summary>
public sealed class ReferencedCategoryNotFoundException : CatalogApplicationException
{
    public ReferencedCategoryNotFoundException(Guid categoryId)
        : base($"Category '{categoryId}' does not exist.")
    {
        CategoryId = categoryId;
    }

    public Guid CategoryId { get; }
}
