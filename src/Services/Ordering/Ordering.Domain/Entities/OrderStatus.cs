namespace Ordering.Domain.Entities;

/// <summary>
/// The life of an order.
///
///                    ┌── reject ──> Rejected        (no stock)
///                    │
///   Pending ─────────┼── cancel ──> Cancelled
///      │             │
///      └─ reserve ──> InventoryReserved
///                          │
///                          ├── cancel ──> Cancelled
///                          │
///                          └─ startPayment ──> PaymentProcessing
///                                                   │
///                                                   ├── succeed ──> Confirmed
///                                                   └── fail ─────> PaymentFailed
///
/// Four terminal states: Confirmed, PaymentFailed, Cancelled, Rejected.
/// Nothing leaves them.
///
/// Two things worth noticing about the diagram:
///
///   PaymentProcessing CANNOT be cancelled. Money may already be moving; a
///   cancel that races a charge is how you refund something that never
///   completed, or ship something you refunded. The customer waits the few
///   seconds it takes to resolve.
///
///   PaymentFailed is TERMINAL and separate from Cancelled. Both mean "this
///   order will not happen", but they mean it for different reasons, and
///   collapsing them into one status throws away the only record of which.
///   Status is not just a flag - it is the order's history compressed into one
///   column.
/// </summary>
public enum OrderStatus
{
    /// <summary>Created. Nothing has been reserved or charged.</summary>
    Pending = 1,

    /// <summary>Inventory is holding the stock.</summary>
    InventoryReserved = 2,

    /// <summary>A payment attempt is in flight.</summary>
    PaymentProcessing = 3,

    /// <summary>Paid, stock consumed. Terminal.</summary>
    Confirmed = 4,

    /// <summary>The payment was declined. Terminal.</summary>
    PaymentFailed = 5,

    /// <summary>The customer or an administrator called it off. Terminal.</summary>
    Cancelled = 6,

    /// <summary>The system refused it - not enough stock. Terminal.</summary>
    Rejected = 7
}
