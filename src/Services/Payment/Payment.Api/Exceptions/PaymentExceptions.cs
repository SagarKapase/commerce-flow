namespace Payment.Api.Exceptions;

/// <summary>Base type for expected business failures in the Payment service.</summary>
public abstract class PaymentApplicationException : Exception
{
    protected PaymentApplicationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when the payment method token is not one this gateway recognises.
/// Maps to 400 Bad Request.
///
/// Note this is NOT a declined payment. "Your card was refused" and "that is
/// not a card" are different answers: the first is the bank's decision and the
/// customer might fix it with a different card; the second means the request
/// itself was malformed. Collapsing them would tell a customer their card was
/// declined when the real problem was a typo in an integration.
/// </summary>
public sealed class UnknownPaymentMethodException : PaymentApplicationException
{
    public UnknownPaymentMethodException(string token)
        : base($"'{token}' is not a recognised payment method token. " +
               $"Use one of: {PaymentMethodTokenList}.")
    {
        Token = token;
    }

    public string Token { get; }

    private const string PaymentMethodTokenList =
        "tok_success, tok_declined, tok_insufficient_funds, tok_timeout";
}

/// <summary>
/// Thrown when a caller reuses a payment id that already exists against a
/// DIFFERENT order. Maps to 409 Conflict.
///
/// This is the one thing that can go wrong once the caller chooses the id, and
/// it must never be answered by silently returning the existing payment: the
/// caller would take "already charged" as meaning THEIR order is paid, when the
/// money actually moved for somebody else's. Refusing loudly is the only safe
/// answer, because the alternative marks an unpaid order as paid.
/// </summary>
public sealed class PaymentIdAlreadyUsedException : PaymentApplicationException
{
    public PaymentIdAlreadyUsedException(Guid paymentId, Guid existingOrderId, Guid requestedOrderId)
        : base($"Payment '{paymentId}' already exists for order '{existingOrderId}' " +
               $"and cannot be reused for order '{requestedOrderId}'.")
    {
        PaymentId = paymentId;
        ExistingOrderId = existingOrderId;
        RequestedOrderId = requestedOrderId;
    }

    public Guid PaymentId { get; }

    public Guid ExistingOrderId { get; }

    public Guid RequestedOrderId { get; }
}
