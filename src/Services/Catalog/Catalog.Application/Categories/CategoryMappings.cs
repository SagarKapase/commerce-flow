using Catalog.Application.Categories.Dtos;
using Catalog.Domain.Entities;

namespace Catalog.Application.Categories;

/// <summary>
/// Entity -> response mapping, written by hand.
///
/// WHY NOT AutoMapper:
/// 1. When a property is renamed, this file fails to compile. AutoMapper fails
///    at runtime, in production, on the one field nobody tested.
/// 2. Stepping into this method in the debugger shows you exactly what happened.
///    Stepping into AutoMapper shows you AutoMapper.
/// 3. The whole mapping is six lines. The library would be bigger than the
///    problem.
/// </summary>
internal static class CategoryMappings
{
    public static CategoryResponse ToResponse(Category category)
    {
        return new CategoryResponse(
            category.Id,
            category.Name,
            category.Slug,
            category.IsActive,
            category.CreatedAtUtc,
            category.UpdatedAtUtc);
    }
}
