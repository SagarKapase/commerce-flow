using Catalog.Application.Common;
using Catalog.Application.Products.Dtos;

namespace Catalog.Application.Products;

public interface IProductService
{
    Task<PagedResponse<ProductResponse>> GetAsync(
        ProductListQuery query,
        CancellationToken cancellationToken);

    /// <returns>The product, or <c>null</c> when no product has that id.</returns>
    Task<ProductResponse?> GetByIdAsync(Guid productId, CancellationToken cancellationToken);

    Task<ProductResponse> CreateAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken);

    /// <returns>The updated product, or <c>null</c> when no product has that id.</returns>
    Task<ProductResponse?> UpdateAsync(
        Guid productId,
        UpdateProductRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Soft-deletes a product by clearing its IsActive flag. The row is kept
    /// because past orders point at it.
    /// </summary>
    /// <returns><c>true</c> if the product exists and is now inactive; <c>false</c> if it was not found.</returns>
    Task<bool> DeactivateAsync(Guid productId, CancellationToken cancellationToken);
}
