using System.ComponentModel.DataAnnotations;
using Catalog.Application.Common.Validation;
using Catalog.Domain.Entities;

namespace Catalog.Application.Products.Dtos;

/// <summary>The body of POST /api/products.</summary>
public sealed class CreateProductRequest
{
    [Required(ErrorMessage = "Product name is required.")]
    [StringLength(Product.NameMaxLength, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional. No [Required], so an empty description is accepted.</summary>
    [StringLength(Product.DescriptionMaxLength)]
    public string Description { get; init; } = string.Empty;

    [Required(ErrorMessage = "Product SKU is required.")]
    [StringLength(Product.SkuMaxLength, MinimumLength = 3)]
    [RegularExpression(
        "^[A-Za-z0-9-]+$",
        ErrorMessage = "SKU may contain only letters, digits and hyphens.")]
    public string Sku { get; init; } = string.Empty;

    /// <summary>Price in major units, e.g. 89.99. See <see cref="MoneyAttribute"/>.</summary>
    [Money]
    public decimal Price { get; init; }

    /// <summary>
    /// No [Required] here: CategoryId is a non-nullable Guid, so it is never
    /// null and [Required] would always pass - even for an all-zero Guid.
    /// An unknown (or empty) category is caught by the application service,
    /// which looks it up and throws ReferencedCategoryNotFoundException.
    /// </summary>
    public Guid CategoryId { get; init; }
}
