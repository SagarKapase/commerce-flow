using System.ComponentModel.DataAnnotations;

namespace Inventory.Application.Stock.Dtos;

// NOTE ON THE FOLDER NAME: this is Stock/, not Inventory/.
//
// A namespace of Inventory.Application.Inventory would make the segment
// "Inventory" ambiguous inside these files - the compiler could not tell
// whether "Inventory.Domain.Entities" meant the root namespace or the nested
// one. Exactly the collision that forced CustomerBasket to be named
// CustomerBasket in the Basket service.
//
// "Stock" is also the better word: the service is Inventory, the thing it
// tracks is stock.

/// <summary>
/// The stock position for one product, as the API reports it.
///
/// TotalQuantity is included even though it is just Available + Reserved,
/// because the sum is the number a warehouse operator cares about and making
/// every client compute it is three chances to compute it differently.
/// </summary>
public sealed record InventoryItemResponse(
    Guid ProductId,
    int AvailableQuantity,
    int ReservedQuantity,
    int TotalQuantity,
    int Version,
    DateTime? UpdatedAtUtc);

/// <summary>
/// The body of POST /api/inventory/adjustments.
///
/// One endpoint handles receiving stock and writing it off, because they are
/// the same operation with a different sign. Two endpoints would be two code
/// paths enforcing the same "never go below zero" rule.
/// </summary>
public sealed class StockAdjustmentRequest
{
    public Guid ProductId { get; init; }

    /// <summary>
    /// Positive receives stock, negative writes it off. Zero is rejected -
    /// an adjustment that changes nothing is a mistake, not a no-op.
    /// </summary>
    [Range(-100_000, 100_000)]
    public int QuantityChange { get; init; }

    /// <summary>
    /// Why. Required, because "the count is wrong and nobody wrote down why"
    /// is how stock records stop being trustworthy. Today it only reaches the
    /// log; a production system would append it to an audit ledger.
    /// </summary>
    [Required]
    [StringLength(200, MinimumLength = 3)]
    public string Reason { get; init; } = string.Empty;
}
