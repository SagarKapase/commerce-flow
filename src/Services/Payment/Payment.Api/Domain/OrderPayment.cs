namespace Payment.Api.Domain;

/// <summary>
/// The record of charging one order.
///
/// NAMED OrderPayment, NOT Payment. The project's root namespace is
/// Payment.Api, so a type called Payment would collide with the namespace
/// segment and make half the file ambiguous to the compiler. Third time this
/// has come up - CustomerBasket in the Basket service, the Stock folder in
/// Inventory, and now here. Namespace/type collisions are a real design
/// constraint, and the fix is always to name the type more precisely rather
/// than to fight the compiler.
///
/// "OrderPayment" is also more honest than "Payment": this row is not money,
/// it is the record of an attempt to move money for a specific order.
/// </summary>
public sealed class OrderPayment
{
    public const int ProviderReferenceMaxLength = 100;
    public const int FailureReasonMaxLength = 300;

    private OrderPayment()
    {
    }

    private OrderPayment(
        Guid id,
        Guid orderId,
        Guid customerId,
        decimal amount,
        DateTime createdAtUtc)
    {
        Id = id;
        OrderId = orderId;
        CustomerId = customerId;
        Amount = amount;
        Status = PaymentStatus.Pending;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// The order being paid for.
    ///
    /// UNIQUE IN THE DATABASE - see PaymentConfiguration. That index is the
    /// single most important line in this service: it makes charging the same
    /// order twice IMPOSSIBLE, not merely unlikely.
    ///
    /// Double-charging is the one bug a payment system is not allowed to have.
    /// A duplicate order is embarrassing; a duplicate charge is a chargeback, a
    /// support call, and a customer who does not come back.
    ///
    /// And, as everywhere in this system, there is no foreign key - orders live
    /// in ordering.db, which this service cannot see.
    /// </summary>
    public Guid OrderId { get; private set; }

    public Guid CustomerId { get; private set; }

    /// <summary>
    /// What we were asked to charge.
    ///
    /// KNOWN GAP, FLAGGED DELIBERATELY: Payment takes this on trust from its
    /// caller. It cannot verify the amount against the order without calling
    /// Ordering, and Ordering is what called us - a circular dependency that
    /// would deadlock the design, not just the code.
    ///
    /// The real answer is that these endpoints are INTERNAL. No customer should
    /// be able to reach POST /api/payments directly, and in Phase 10 the API
    /// Gateway simply will not route to it. Same open question as Inventory's
    /// reservation endpoints, and it gets the same answer.
    /// </summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>
    /// The gateway's own identifier for the transaction - what you quote to
    /// the provider's support desk when a customer disputes a charge. Null
    /// until the gateway answers.
    /// </summary>
    public string? ProviderReference { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>
    /// The id is supplied by the CALLER, not generated here - see
    /// CreatePaymentRequest.PaymentId for why. The mapping sets
    /// ValueGeneratedNever() so EF does not try to fill it in either.
    /// </summary>
    public static OrderPayment Create(Guid id, Guid orderId, Guid customerId, decimal amount)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A payment must have an id.", nameof(id));
        }

        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("A payment must belong to an order.", nameof(orderId));
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "A payment amount must be greater than zero.");
        }

        return new OrderPayment(
            id,
            orderId,
            customerId,
            amount,
            DateTime.UtcNow);
    }

    /// <summary>The gateway accepted it.</summary>
    /// <returns><c>false</c> if this payment had already succeeded.</returns>
    public bool MarkSucceeded(string providerReference)
    {
        if (Status == PaymentStatus.Succeeded)
        {
            return false;
        }

        if (Status == PaymentStatus.Failed)
        {
            // A declined payment does not quietly become a successful one
            // because a late message arrived. Retrying means a NEW attempt.
            throw new InvalidOperationException(
                $"Payment '{Id}' already failed and cannot be marked as succeeded.");
        }

        Status = PaymentStatus.Succeeded;
        ProviderReference = providerReference;
        CompletedAtUtc = DateTime.UtcNow;

        return true;
    }

    /// <summary>The gateway declined it.</summary>
    /// <returns><c>false</c> if this payment had already failed.</returns>
    public bool MarkFailed(string reason)
    {
        if (Status == PaymentStatus.Failed)
        {
            return false;
        }

        if (Status == PaymentStatus.Succeeded)
        {
            // The money moved. Undoing that is a REFUND - a separate business
            // process with its own record - not a status column changing.
            throw new InvalidOperationException(
                $"Payment '{Id}' already succeeded and cannot be marked as failed.");
        }

        Status = PaymentStatus.Failed;
        FailureReason = reason;
        CompletedAtUtc = DateTime.UtcNow;

        return true;
    }
}
