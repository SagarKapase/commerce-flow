using Catalog.Application.Products.Dtos;
using Catalog.Domain.Entities;

namespace Catalog.Application.Products;

internal static class ProductMappings
{
    /// <summary>
    /// Maps a product to its API response.
    ///
    /// The category name is passed in rather than read from product.Category on
    /// purpose. The navigation property is only populated when a query asks for
    /// it, so relying on it here would work in one code path and quietly throw a
    /// NullReferenceException in another. Making it a parameter means the
    /// compiler forces every caller to have obtained the name legitimately.
    /// </summary>
    public static ProductResponse ToResponse(Product product, string categoryName)
    {
        return new ProductResponse(
            product.Id,
            product.Name,
            product.Description,
            product.Sku,
            product.Price,
            product.CategoryId,
            categoryName,
            product.IsActive,
            product.CreatedAtUtc,
            product.UpdatedAtUtc);
    }
}
