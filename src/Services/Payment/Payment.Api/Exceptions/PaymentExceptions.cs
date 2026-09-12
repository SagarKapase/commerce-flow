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
