using System.ComponentModel.DataAnnotations;
using Catalog.Domain.Entities;

namespace Catalog.Application.Categories.Dtos;

/// <summary>
/// The body of POST /api/categories.
///
/// WHY A REQUEST CLASS INSTEAD OF THE Category ENTITY:
/// - The entity has private setters and no public constructor, so a model
///   binder could not fill it in anyway - by design.
/// - Over-posting: if the action took a Category, a caller could send
///   "isActive": true or "createdAtUtc": "1999-01-01" and set fields they have
///   no business setting.
/// - The API contract and the database schema are allowed to evolve separately.
///
/// WHY `init` PROPERTIES AND NOT `required`:
/// System.Text.Json enforces the C# `required` keyword during deserialisation
/// and throws, which surfaces as an opaque JSON error. [Required] instead lets
/// model binding complete, fails validation, and produces a clean
/// 400 ValidationProblemDetails that names the field.
/// </summary>
public sealed class CreateCategoryRequest
{
    [Required(ErrorMessage = "Category name is required.")]
    [StringLength(Category.NameMaxLength, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// URL-friendly identifier, e.g. "mechanical-keyboards".
    /// The regular expression enforces lower-case words separated by single
    /// hyphens - the shape that is safe to put in a URL.
    /// </summary>
    [Required(ErrorMessage = "Category slug is required.")]
    [StringLength(Category.SlugMaxLength, MinimumLength = 2)]
    [RegularExpression(
        "^[a-z0-9]+(-[a-z0-9]+)*$",
        ErrorMessage = "Slug must be lower-case words separated by single hyphens, e.g. 'mechanical-keyboards'.")]
    public string Slug { get; init; } = string.Empty;
}
