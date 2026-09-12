namespace Inventory.Domain.Exceptions;

/// <summary>
/// Thrown when a reservation asks for more units than are available.
///
/// NOTE WHERE THIS LIVES: Inventory.Domain, not Inventory.Application.
///
/// Catalog put its exceptions in the Application layer because they were about
/// use cases - "that SKU is taken", "that category has products". This one is
/// different in kind: "you cannot reserve stock you do not have" is the rule
/// that DEFINES what inventory means. It belongs to the entity that enforces
/// it, and it travels with that entity wherever it is used.
///
/// The test for which layer an exception belongs to: could you delete the API
/// and still need it? Yes - so it is domain.
/// </summary>
public sealed class InsufficientStockException : Exception
{
    public InsufficientStockException(Guid productId, int requested, int available)
        : base($"Cannot reserve {requested} unit(s) of product '{productId}': only {available} available.")
    {
        ProductId = productId;
        Requested = requested;
        Available = available;
    }

    public Guid ProductId { get; }

    public int Requested { get; }

    public int Available { get; }
}
