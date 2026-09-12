namespace Catalog.Application.Categories.Dtos;

/// <summary>
/// What the API returns for a category.
///
/// A positional record: immutable, value-equality, and the whole contract is
/// visible on one screen. Requests are classes (they need attributes on each
/// property); responses are records (they need nothing but shape).
/// </summary>
public sealed record CategoryResponse(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
