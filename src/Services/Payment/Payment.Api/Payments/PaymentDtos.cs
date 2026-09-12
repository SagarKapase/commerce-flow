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
public sealed class CreatePaymentRequest
{
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
