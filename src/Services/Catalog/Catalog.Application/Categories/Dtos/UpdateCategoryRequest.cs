using System.ComponentModel.DataAnnotations;
using Catalog.Domain.Entities;

namespace Catalog.Application.Categories.Dtos;

/// <summary>
/// The body of PUT /api/categories/{id}.
///
/// It looks identical to CreateCategoryRequest today, and that is fine - they
/// are two different contracts that happen to agree right now. Merging them
/// would mean that the moment one gains a field (say, a create-only "template"
/// flag) the other silently gains it too.
///
/// Notice what is NOT here: IsActive. Activation is a business operation, not a
/// field edit, so it gets its own endpoint (DELETE deactivates). A PUT that can
/// silently resurrect a disabled category is exactly the kind of accidental
/// power we are avoiding.
/// </summary>
public sealed class UpdateCategoryRequest
{
    [Required(ErrorMessage = "Category name is required.")]
    [StringLength(Category.NameMaxLength, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [Required(ErrorMessage = "Category slug is required.")]
    [StringLength(Category.SlugMaxLength, MinimumLength = 2)]
    [RegularExpression(
        "^[a-z0-9]+(-[a-z0-9]+)*$",
        ErrorMessage = "Slug must be lower-case words separated by single hyphens, e.g. 'mechanical-keyboards'.")]
    public string Slug { get; init; } = string.Empty;
}
