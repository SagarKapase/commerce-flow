namespace Ordering.Domain.Entities;

/// <summary>
/// One line of an order: a product, a quantity, and what it cost AT THE MOMENT
/// THE ORDER WAS PLACED.
///
/// ============ WHY THE NAME AND PRICE ARE COPIED HERE ============
/// Catalog owns products, in a different database, and its prices change.
/// If this row stored only a ProductId and the invoice looked the price up at
/// print time, then a price rise next Tuesday would silently rewrite what a
/// customer paid last Monday. Every total in every historical order would drift
/// with the price list, and no report would ever reconcile.
///
/// So an order line is a SNAPSHOT, and deliberately so: it records a fact about
/// the past. The Catalog can rename the product, discontinue it, or triple the
/// price, and this row keeps saying what was actually agreed.
///
/// This is not a workaround for microservices. A monolith with one database
/// should do exactly the same thing - the difference is that separate databases
/// make it impossible to get wrong by accident, because there is no join
/// available to tempt you.
/// ================================================================
///
/// The line is IMMUTABLE once created. There is no ChangeQuantity here: an
/// order that has been placed is not an editable document. Wanting a different
/// quantity means cancelling and placing another order, which is also what any
/// real shop makes you do.
/// </summary>
public sealed class OrderItem
{
    public const int ProductNameMaxLength = 200;

    private OrderItem()
    {
        ProductName = null!;
    }

    internal OrderItem(
        Guid orderId,
        Guid productId,
        string productName,
        decimal unitPrice,
        int quantity)
    {
        OrderId = orderId;
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    /// <summary>Part of the composite key, and the foreign key to the order.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>
    /// Part of the composite key. A product appears at most once per order -
    /// two of the same thing is one line with a quantity of two.
    ///
    /// No foreign key: this points into catalog.db, which this service cannot
    /// see.
    /// </summary>
    public Guid ProductId { get; private set; }

    public string ProductName { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    public decimal LineTotal => UnitPrice * Quantity;
}
