namespace Catalog.Application.Exceptions;

/// <summary>
/// Thrown when a category slug is already taken. Slugs appear in URLs, so they
/// must be unique. Maps to 409 Conflict.
/// </summary>
public sealed class DuplicateCategorySlugException : CatalogApplicationException
{
    public DuplicateCategorySlugException(string slug)
        : base($"A category with slug '{slug}' already exists.")
    {
        Slug = slug;
    }

    public string Slug { get; }
}
