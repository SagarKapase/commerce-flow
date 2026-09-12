using Inventory.Application.Stock.Dtos;

namespace Inventory.Application.Stock;

public interface IStockService
{
    /// <returns>The stock record, or <c>null</c> if this product has never been counted.</returns>
    Task<InventoryItemResponse?> GetAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// Receives or writes off stock. Creates the stock record on the first
    /// positive adjustment for a product - that is how stock first enters the
    /// system, so there is no separate "create product stock" endpoint to
    /// forget to call.
    /// </summary>
    Task<InventoryItemResponse> AdjustAsync(
        StockAdjustmentRequest request,
        CancellationToken cancellationToken);
}
