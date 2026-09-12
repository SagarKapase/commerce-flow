namespace Ordering.Application.Abstractions;

/// <summary>
/// What the payment provider decided.
///
/// Three outcomes again, split the same way as the stock reservation:
/// "charged" and "declined" are both ANSWERS and become a result; "could not
/// ask" is an exception.
///
/// The distinction matters more here than anywhere else in the system. A
/// declined card must show the customer a message and release their stock; an
/// unreachable payment service must NOT, because the charge may have gone
/// through and releasing the stock would leave a paid order with nothing
/// reserved for it.
/// </summary>
public sealed record PaymentResult
{
    private PaymentResult(bool succeeded, Guid paymentId, string? failureReason)
    {
        Succeeded = succeeded;
        PaymentId = paymentId;
        FailureReason = failureReason;
    }

    public bool Succeeded { get; }

    /// <summary>
    /// Set for BOTH outcomes. A declined payment is still a payment record -
    /// the order links to it either way, so a customer can see which card was
    /// refused and a finance team can reconcile.
    /// </summary>
    public Guid PaymentId { get; }

    public string? FailureReason { get; }

    public static PaymentResult Charged(Guid paymentId) => new(true, paymentId, null);

    public static PaymentResult Declined(Guid paymentId, string reason) =>
        new(false, paymentId, reason);
}

/// <summary>Ordering's view of the Payment service.</summary>
public interface IPaymentClient
{
    /// <exception cref="Exceptions.DownstreamUnavailableException">
    /// Payment could not be reached or did not answer in time.
    ///
    /// READ THIS CAREFULLY, because it is the most dangerous exception in the
    /// project: a timeout does NOT mean the money did not move. Payment may
    /// have charged the card and lost the response on the way back. Anything
    /// the caller does next has to be safe under that uncertainty.
    /// </exception>
    Task<PaymentResult> ChargeAsync(
        Guid orderId,
        Guid customerId,
        decimal amount,
        string paymentMethodToken,
        CancellationToken cancellationToken);
}
