using System.ComponentModel.DataAnnotations;

namespace Payment.Api.Payments;

/// <summary>
/// The body of POST /api/payments.
///
/// The caller is the ORDERING SERVICE, not a browser. Ordering knows the order
/// id, the customer and the amount because it computed them from the basket;
/// the customer supplies only the payment method token, which travels through
/// Ordering unchanged.
/// </summary>
public sealed class CreatePaymentRequest : IValidatableObject
{
    /// <summary>
    /// THE CALLER CHOOSES THE PAYMENT ID. This service does not mint one.
    ///
    /// Ordering writes this id onto its own order row and saves it BEFORE
    /// calling us, so that an order stuck in PaymentProcessing carries the
    /// exact id needed to ask "did that charge actually happen?" against
    /// GET /api/payments/{id}. If we minted the id here, the answer would
    /// arrive in a response - and the response is precisely what gets lost
    /// in the failure this is meant to survive.
    ///
    /// That is the whole argument for a caller-chosen identifier: it is
    /// known to the caller before the risky operation starts, so it is still
    /// known afterwards regardless of what came back.
    /// </summary>
    public Guid PaymentId { get; init; }

    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    /// <summary>
    /// Taken on trust from the caller - see OrderPayment.Amount for why we
    /// cannot verify it, and why the answer is that this endpoint must not be
    /// publicly reachable.
    /// </summary>
    [Range(0.01, 10_000_000)]
    public decimal Amount { get; init; }

    /// <summary>
    /// One of the test tokens in PaymentMethodTokens. In a real system this
    /// would be a single-use token minted by the provider's client-side SDK -
    /// which is the point of tokenisation: card numbers never touch your
    /// servers, so they cannot leak from them.
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string PaymentMethodToken { get; init; } = string.Empty;

    /// <summary>
    /// [Required] cannot express "not the default" for a Guid - a struct is
    /// always present, so an omitted field arrives as Guid.Empty and passes.
    /// IValidatableObject is the hook that turns that into a 400 naming the
    /// field, rather than a 500 from the domain constructor further down.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PaymentId == Guid.Empty)
        {
            yield return new ValidationResult(
                "A payment id must be supplied by the caller.", [nameof(PaymentId)]);
        }

        if (OrderId == Guid.Empty)
        {
            yield return new ValidationResult(
                "A payment must belong to an order.", [nameof(OrderId)]);
        }
    }
}

public sealed record PaymentResponse(
    Guid Id,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Status,
    string? ProviderReference,
    string? FailureReason,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);
