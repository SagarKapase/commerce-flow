using Ordering.Domain.Exceptions;
using Ordering.Domain.ValueObjects;

namespace Ordering.Domain.Entities;

/// <summary>
/// A customer's commitment to buy.
///
/// This is the richest aggregate in CommerceFlow and the reason Ordering is a
/// four-project service. Everything that can happen to an order happens through
/// a method on this class, and every one of those methods can refuse.
///
/// ============ THE RULE THAT SHAPES THE WHOLE CLASS ============
/// Status has a PRIVATE setter and there is no SetStatus method.
///
/// The alternative - a public settable Status - looks harmless and is the
/// single most common way order workflows rot. Six months in, some code path
/// somewhere sets Confirmed on an order whose payment failed, and there is
/// nowhere to put a breakpoint because the assignment is one line in a service
/// nobody remembers writing. Making the transitions methods means every legal
/// change to an order's life has a NAME, a set of preconditions, and exactly
/// one place to look.
///
/// Read the methods in order and you have read the business process.
/// ==============================================================
/// </summary>
public sealed class Order
{
    public const int MaxItems = 100;

    private readonly List<OrderItem> _items = new();

    private Order()
    {
        ShippingAddress = null!;
    }

    private Order(Guid id, Guid customerId, Address shippingAddress, DateTime createdAtUtc)
    {
        Id = id;
        CustomerId = customerId;
        ShippingAddress = shippingAddress;
        Status = OrderStatus.Pending;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Who placed it. Set once, at creation, from the token - never from a
    /// request body, and never changed afterwards. An order does not move
    /// between customers.
    /// </summary>
    public Guid CustomerId { get; private set; }

    public OrderStatus Status { get; private set; }

    public Address ShippingAddress { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    /// <summary>
    /// COMPUTED, not stored.
    ///
    /// The lines are immutable, so the total can never disagree with them - and
    /// a stored copy is one more thing that can drift out of step with the data
    /// it summarises.
    ///
    /// This changes the day the total stops being derivable from the lines:
    /// discounts, tax, shipping, currency conversion. At that point the total
    /// is its own fact rather than a sum, and it gets a column. Storing a
    /// derived value before then is denormalisation without a reason.
    /// </summary>
    public decimal TotalAmount => _items.Sum(item => item.LineTotal);

    /// <summary>
    /// The hold Inventory is keeping for this order. Null until Phase 8 wires
    /// the two services together - the field exists now because
    /// MarkInventoryReserved cannot do its job without somewhere to put it.
    /// </summary>
    public Guid? InventoryReservationId { get; private set; }

    /// <summary>The payment attempt. Null until Phase 9.</summary>
    public Guid? PaymentId { get; private set; }

    /// <summary>
    /// Why the order ended badly, in the customer's words rather than a stack
    /// trace. Populated by Reject and MarkPaymentFailed.
    /// </summary>
    public string? FailureReason { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Places an order.
    /// </summary>
    /// <remarks>
    /// Every line is copied in as name, price and quantity. The caller supplies
    /// those values, and in Phase 8 the caller will be Ordering itself, reading
    /// them out of the customer's basket rather than out of an HTTP body.
    /// </remarks>
    public static Order Create(
        Guid customerId,
        Address shippingAddress,
        IReadOnlyCollection<(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity)> items)
    {
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("An order must belong to a customer.", nameof(customerId));
        }

        if (items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }

        if (items.Count > MaxItems)
        {
            throw new ArgumentException(
                $"An order cannot contain more than {MaxItems} distinct products.", nameof(items));
        }

        if (items.GroupBy(item => item.ProductId).Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "An order cannot list the same product twice.", nameof(items));
        }

        var order = new Order(
            Guid.CreateVersion7(),
            customerId,
            shippingAddress,
            DateTime.UtcNow);

        foreach (var (productId, productName, unitPrice, quantity) in items)
        {
            if (quantity < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(items), quantity, "Order quantities must be at least 1.");
            }

            if (unitPrice <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(items), unitPrice, "Order prices must be greater than zero.");
            }

            order._items.Add(new OrderItem(order.Id, productId, productName, unitPrice, quantity));
        }

        return order;
    }

    /// <summary>
    /// Inventory is now holding the stock. Pending -> InventoryReserved.
    /// </summary>
    public void MarkInventoryReserved(Guid reservationId)
    {
        RequireStatus(OrderStatus.Pending, "marked as inventory-reserved");

        InventoryReservationId = reservationId;
        Status = OrderStatus.InventoryReserved;

        Touch();
    }

    /// <summary>
    /// Inventory refused - not enough stock. Pending -> Rejected.
    ///
    /// A separate terminal state from Cancelled because "we could not fulfil
    /// this" and "the customer changed their mind" are different events, and a
    /// shop needs to be able to count them separately.
    /// </summary>
    public void Reject(string reason)
    {
        RequireStatus(OrderStatus.Pending, "rejected");

        FailureReason = reason;
        Status = OrderStatus.Rejected;

        Touch();
    }

    /// <summary>
    /// A payment attempt is in flight. InventoryReserved -> PaymentProcessing.
    ///
    /// Note the precondition: you cannot charge for an order whose stock is not
    /// held. Taking money for goods that might not exist is the one ordering
    /// mistake customers genuinely never forgive.
    /// </summary>
    public void StartPayment(Guid paymentId)
    {
        RequireStatus(OrderStatus.InventoryReserved, "sent for payment");

        PaymentId = paymentId;
        Status = OrderStatus.PaymentProcessing;

        Touch();
    }

    /// <summary>
    /// Paid. PaymentProcessing -> Confirmed. Terminal.
    /// </summary>
    /// <returns>
    /// <c>false</c> if the order was already Confirmed - the second delivery of
    /// a "payment succeeded" message, which a broker guaranteeing at-least-once
    /// WILL send eventually. Same idempotency pattern as InventoryReservation:
    /// the caller uses the bool to decide whether to do the follow-up work, so
    /// nothing is applied twice.
    /// </returns>
    public bool Confirm()
    {
        if (Status == OrderStatus.Confirmed)
        {
            return false;
        }

        RequireStatus(OrderStatus.PaymentProcessing, "confirmed");

        Status = OrderStatus.Confirmed;

        Touch();

        return true;
    }

    /// <summary>
    /// The payment was declined. PaymentProcessing -> PaymentFailed. Terminal.
    ///
    /// In Phase 11 this is the point where the saga compensates: the inventory
    /// reservation is released so the stock goes back on the shelf. Note that
    /// the order does NOT return to Pending. Compensation undoes the side
    /// effects; it does not rewind time.
    /// </summary>
    public bool MarkPaymentFailed(string reason)
    {
        if (Status == OrderStatus.PaymentFailed)
        {
            return false;
        }

        RequireStatus(OrderStatus.PaymentProcessing, "marked as payment-failed");

        FailureReason = reason;
        Status = OrderStatus.PaymentFailed;

        Touch();

        return true;
    }

    /// <summary>
    /// The customer called it off. Pending or InventoryReserved -> Cancelled.
    /// </summary>
    /// <remarks>
    /// The two allowed states are the whole rule, and each exclusion is a
    /// decision:
    ///
    ///   PaymentProcessing - refused. Money may be moving. A cancel racing a
    ///     charge is how you end up refunding something that never completed,
    ///     or shipping something you already refunded.
    ///
    ///   Confirmed - refused. The order is paid and the stock is consumed.
    ///     Undoing that is a refund and a return: a different business process,
    ///     with different paperwork, that should not be reachable by flipping
    ///     one column.
    ///
    ///   Cancelled, Rejected, PaymentFailed - already over.
    /// </remarks>
    public void Cancel()
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.InventoryReserved))
        {
            throw new InvalidOrderStateTransitionException(Id, Status, "cancelled");
        }

        Status = OrderStatus.Cancelled;

        Touch();
    }

    /// <summary>Can this order still be cancelled? Lets a UI hide a button that would 409.</summary>
    public bool CanBeCancelled =>
        Status is OrderStatus.Pending or OrderStatus.InventoryReserved;

    private void RequireStatus(OrderStatus required, string attemptedTransition)
    {
        if (Status != required)
        {
            throw new InvalidOrderStateTransitionException(Id, Status, attemptedTransition);
        }
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
