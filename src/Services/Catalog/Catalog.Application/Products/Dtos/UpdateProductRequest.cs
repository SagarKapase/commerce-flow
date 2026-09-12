using System.ComponentModel.DataAnnotations;
using Catalog.Application.Common.Validation;
using Catalog.Domain.Entities;

namespace Catalog.Application.Products.Dtos;

/// <summary>
/// The body of PUT /api/products/{id}.
///
/// PUT replaces the editable fields of the resource, so every field is sent
/// every time. IsActive is not editable here - that is what DELETE (deactivate)
/// is for.
/// </summary>
public sealed class UpdateProductRequest
{
    [Required(ErrorMessage = "Product name is required.")]
    [StringLength(Product.NameMaxLength, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [StringLength(Product.DescriptionMaxLength)]
    public string Description { get; init; } = string.Empty;

    [Required(ErrorMessage = "Product SKU is required.")]
    [StringLength(Product.SkuMaxLength, MinimumLength = 3)]
    [RegularExpression(
        "^[A-Za-z0-9-]+$",
        ErrorMessage = "SKU may contain only letters, digits and hyphens.")]
    public string Sku { get; init; } = string.Empty;

    [Money]
    public decimal Price { get; init; }

    public Guid CategoryId { get; init; }
}
