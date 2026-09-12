using Inventory.Domain.Exceptions;

namespace Inventory.Domain.Entities;

/// <summary>
/// The stock position for one product.
///
/// THE CENTRAL IDEA OF THIS SERVICE - stock lives in two buckets:
///
///   AvailableQuantity  units anybody may still buy
///   ReservedQuantity   units promised to an order that has not been paid for
///
///   TotalQuantity = Available + Reserved = what is physically on the shelf
///
/// Reserving does NOT reduce total stock. It moves units from Available to
/// Reserved - the goods have not left the building, they are just spoken for.
/// CONFIRMING is what actually reduces the total, because that is when the
/// goods ship. RELEASING moves them back.
///
/// Get this model wrong and you either oversell (only tracking a single number
/// and decrementing it optimistically) or you lose stock forever (decrementing
/// on reserve and forgetting to restore it when payment fails). Both are real
/// bugs that real shops have shipped.
/// </summary>
public sealed class InventoryItem
{
    private InventoryItem()
    {
    }

    private InventoryItem(Guid productId, int availableQuantity, DateTime createdAtUtc)
    {
        ProductId = productId;
        AvailableQuantity = availableQuantity;
        ReservedQuantity = 0;
        Version = 1;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// The primary key. Same reasoning as the basket's user id: there is
    /// exactly one stock record per product, so the thing it is about IS its
    /// identity. No surrogate key that nothing would ever reference.
    ///
    /// And, as everywhere else in this system, this Guid points at a row in
    /// ANOTHER service's database with no foreign key to enforce it.
    /// </summary>
    public Guid ProductId { get; private set; }

    public int AvailableQuantity { get; private set; }

    public int ReservedQuantity { get; private set; }

    public int TotalQuantity => AvailableQuantity + ReservedQuantity;

    /// <summary>
    /// The optimistic concurrency token. Incremented by every method below that
    /// changes anything.
    ///
    /// WHY IT IS HERE, IN A DOMAIN PROJECT THAT REFERENCES NOTHING:
    /// A version column is arguably a persistence concern, and a purist would
    /// hide it behind an EF interceptor that bumps it invisibly. We keep it
    /// visible because it is the single mechanism this phase exists to teach,
    /// and a mechanism you cannot see is a mechanism you cannot debug.
    ///
    /// SQLite has no rowversion type, so there is nothing to hide behind
    /// anyway - and doing it by hand means the same model works identically on
    /// PostgreSQL, SQL Server or anything else.
    /// </summary>
    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public static InventoryItem Create(Guid productId, int initialQuantity)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("A stock record must belong to a product.", nameof(productId));
        }

        if (initialQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialQuantity), initialQuantity, "Initial quantity cannot be negative.");
        }

        return new InventoryItem(productId, initialQuantity, DateTime.UtcNow);
    }

    /// <summary>
    /// Moves units from Available to Reserved.
    /// </summary>
    /// <exception cref="InsufficientStockException">
    /// Not enough available. This is the check that stops overselling - and on
    /// its own it is NOT enough, because two callers can both pass it before
    /// either saves. The Version token is what closes that window.
    /// </exception>
    public void Reserve(int quantity)
    {
        ValidatePositive(quantity);

        if (quantity > AvailableQuantity)
        {
            throw new InsufficientStockException(ProductId, quantity, AvailableQuantity);
        }

        AvailableQuantity -= quantity;
        ReservedQuantity += quantity;

        Touch();
    }

    /// <summary>
    /// Gives reserved units back to the available pool - the compensation for a
    /// reservation that will not be fulfilled (payment failed, order cancelled).
    ///
    /// This is the operation a saga calls to undo itself in Phase 11, which is
    /// why it must never fail for a reason the caller cannot fix.
    /// </summary>
    public void Release(int quantity)
    {
        ValidatePositive(quantity);

        if (quantity > ReservedQuantity)
        {
            // Releasing more than is reserved would invent stock out of nothing.
            // If this ever throws, we have a bookkeeping bug, not a user error.
            throw new InvalidOperationException(
                $"Cannot release {quantity} unit(s) of product '{ProductId}': " +
                $"only {ReservedQuantity} reserved.");
        }

        ReservedQuantity -= quantity;
        AvailableQuantity += quantity;

        Touch();
    }

    /// <summary>
    /// The goods have shipped. Reserved units leave the building for good, so
    /// this is the ONLY operation that reduces TotalQuantity.
    /// </summary>
    public void ConfirmReservation(int quantity)
    {
        ValidatePositive(quantity);

        if (quantity > ReservedQuantity)
        {
            throw new InvalidOperationException(
                $"Cannot confirm {quantity} unit(s) of product '{ProductId}': " +
                $"only {ReservedQuantity} reserved.");
        }

        ReservedQuantity -= quantity;

        Touch();
    }

    /// <summary>
    /// A stock correction: goods received, damaged, recounted.
    /// </summary>
    /// <param name="quantityChange">Positive adds stock, negative removes it.</param>
    public void Adjust(int quantityChange)
    {
        if (quantityChange == 0)
        {
            throw new ArgumentException("A stock adjustment must change something.", nameof(quantityChange));
        }

        var newAvailable = AvailableQuantity + quantityChange;

        if (newAvailable < 0)
        {
            // You cannot remove stock that is already promised to somebody.
            // Reserved units are untouchable by an adjustment - taking them
            // would oversell an order that has already been accepted.
            throw new InsufficientStockException(ProductId, -quantityChange, AvailableQuantity);
        }

        AvailableQuantity = newAvailable;

        Touch();
    }

    private void Touch()
    {
        // The version bump and the timestamp always travel together: every
        // change to this row is a new version of it.
        Version++;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static void ValidatePositive(int quantity)
    {
        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "Quantity must be at least 1.");
        }
    }
}
