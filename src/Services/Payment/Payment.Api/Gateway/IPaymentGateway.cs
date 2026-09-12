namespace Payment.Api.Gateway;

/// <summary>What the payment provider said.</summary>
public sealed record PaymentGatewayResult
{
    private PaymentGatewayResult(bool succeeded, string? providerReference, string? failureReason)
    {
        Succeeded = succeeded;
        ProviderReference = providerReference;
        FailureReason = failureReason;
    }

    public bool Succeeded { get; }

    /// <summary>The provider's transaction id. Set only on success.</summary>
    public string? ProviderReference { get; }

    /// <summary>Set only on failure, and written for a customer to read.</summary>
    public string? FailureReason { get; }

    public static PaymentGatewayResult Approved(string providerReference) =>
        new(true, providerReference, null);

    public static PaymentGatewayResult Declined(string reason) =>
        new(false, null, reason);
}

/// <summary>
/// The seam between this service and whoever actually moves money.
///
/// ============ THE ONE ABSTRACTION THIS SERVICE NEEDS ============
/// Payment.Api is a single project - no Domain, Application or Infrastructure
/// assemblies - because a simulated payment has almost no domain. Splitting it
/// four ways would produce four files of pass-through code, exactly as it
/// would have for Basket.
///
/// But THIS interface earns its place, by the usual four questions:
///
///   1. What problem exists now? The decision "does this payment succeed" is
///      fake, and one day it will be an HTTPS call to Stripe with retries,
///      webhooks, idempotency keys and a secret.
///   2. What does it solve? PaymentService never learns which. It asks for a
///      charge and gets an answer.
///   3. Why now? Because the fake and the real thing differ in EVERY respect
///      except this method signature, and that is the definition of a good
///      seam.
///   4. Simpler alternative? Put the if-statement inline in PaymentService.
///      Rejected: the day a real provider arrives, the business logic and the
///      HTTP plumbing would have to be untangled from each other first.
///
/// The lesson generalises: the seam that matters is where the world changes,
/// not where an assembly boundary would look tidy.
/// ================================================================
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Attempts to charge the payment method.
    /// </summary>
    /// <exception cref="Exceptions.UnknownPaymentMethodException">
    /// The token is not one this gateway recognises. Note this is an exception
    /// rather than a declined result: "your card was refused" and "that is not
    /// a card" are different answers, and the customer can only act on one.
    /// </exception>
    Task<PaymentGatewayResult> ChargeAsync(
        string paymentMethodToken,
        decimal amount,
        CancellationToken cancellationToken);
}
