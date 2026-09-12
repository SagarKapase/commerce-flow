namespace Catalog.Application.Products.Dtos;

/// <summary>
/// What the API returns for a product.
///
/// CategoryName is included even though the database only stores CategoryId:
/// almost every caller wants to display it, and returning it here saves the
/// client a second round trip. This is a deliberate API design choice, not an
/// accident of the schema - which is precisely why we do not return entities.
/// </summary>
public sealed record ProductResponse(
    Guid Id,
    string Name,
    string Description,
    string Sku,
    decimal Price,
    Guid CategoryId,
    string CategoryName,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
