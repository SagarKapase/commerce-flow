namespace Basket.Api.Domain;

/// <summary>
/// One line in a basket: a product, a quantity, and a snapshot of what it was
/// called and what it cost when it was added.
/// </summary>
public sealed class BasketItem
{
    public const int MaxQuantity = 99;
    public const int ProductNameMaxLength = 200;

    private BasketItem()
    {
        ProductName = null!;
    }

    internal BasketItem(
        Guid userId,
        Guid productId,
        string productName,
        decimal unitPrice,
        int quantity,
        DateTime addedAtUtc)
    {
        UserId = userId;
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        AddedAtUtc = addedAtUtc;
    }

    /// <summary>Part of the composite primary key, and the foreign key to the basket.</summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// A reference to a product owned by the CATALOG service, in a completely
    /// different database file. There is no foreign key here and there cannot
    /// be one - SQLite has no idea catalog.db exists.
    ///
    /// This is what "database per service" actually feels like. The integrity
    /// that a monolith would get free from a FK constraint has to be earned:
    /// in Phase 5 Basket will ask Catalog whether this product exists before
    /// accepting it, and even then the answer can go stale a second later.
    /// </summary>
    public Guid ProductId { get; private set; }

    /// <summary>
    /// A COPY of the product name, not a lookup.
    ///
    /// Basket cannot join to catalog.db, so it stores what it needs. The cost
    /// is that this copy can drift when an admin renames the product; the
    /// benefit is that showing a basket requires no other service to be alive.
    /// That trade - stale data in exchange for independence - is the defining
    /// trade of microservices.
    /// </summary>
    public string ProductName { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    public DateTime AddedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public decimal LineTotal => UnitPrice * Quantity;

    /// <summary>
    /// internal, not public: only CustomerBasket may change a line. Everything
    /// outside this assembly has to go through the aggregate root, which is
    /// what keeps the "at most 50 lines, at most 99 each" rules enforceable.
    /// </summary>
    internal void ChangeQuantity(int quantity, DateTime utcNow)
    {
        Quantity = ValidateQuantity(quantity);
        UpdatedAtUtc = utcNow;
    }

    internal void RefreshProductDetails(string productName, decimal unitPrice, DateTime utcNow)
    {
        ProductName = productName;
        UnitPrice = unitPrice;
        UpdatedAtUtc = utcNow;
    }

    internal static int ValidateQuantity(int quantity)
    {
        // A guard, not the primary defence - the request DTO's [Range] rejects
        // a bad quantity with a 400 long before we get here. If this throws, we
        // called our own domain incorrectly.
        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "Quantity must be at least 1.");
        }

        if (quantity > MaxQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, $"Quantity cannot exceed {MaxQuantity}.");
        }

        return quantity;
    }
}
