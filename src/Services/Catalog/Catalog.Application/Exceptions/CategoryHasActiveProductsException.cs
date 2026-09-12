namespace Catalog.Application.Exceptions;

/// <summary>
/// Thrown when someone tries to deactivate a category that still has active
/// products in it. Deactivating it would leave those products pointing at a
/// category customers can no longer browse.
///
/// This is a BUSINESS rule, not an input rule: nothing about the request is
/// malformed, it just cannot be honoured given the current data. That is
/// exactly what 409 Conflict is for.
/// </summary>
public sealed class CategoryHasActiveProductsException : CatalogApplicationException
{
    public CategoryHasActiveProductsException(Guid categoryId, int activeProductCount)
        : base($"Category '{categoryId}' still has {activeProductCount} active product(s). " +
               "Deactivate them first.")
    {
        CategoryId = categoryId;
        ActiveProductCount = activeProductCount;
    }

    public Guid CategoryId { get; }

    public int ActiveProductCount { get; }
}
