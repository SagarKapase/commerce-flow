namespace Payment.Api.Gateway;

/// <summary>
/// The test tokens this simulated gateway understands.
///
/// ============ WHY EXPLICIT TOKENS, NOT MAGIC AMOUNTS ============
/// A common trick in demo systems is "any amount ending in .13 fails". It
/// works, and it is a bad idea:
///
///   - It is invisible. Nothing in the code says so; you find out by reading
///     a wiki page that is out of date, or by accident.
///   - It collides with reality. Real prices end in .13 sometimes, and then a
///     legitimate order fails for a reason nobody can explain.
///   - It cannot express variety. Declined, insufficient funds and a gateway
///     timeout are three different failures with three different correct
///     responses, and one magic number cannot say which you want.
///
/// Explicit tokens fix all three, and they mirror how the real thing works:
/// Stripe, Adyen and Braintree all ship documented test credentials that
/// produce specific outcomes on demand. Naming the behaviour you want is
/// always better than encoding it in a value that means something else.
/// ================================================================
/// </summary>
public static class PaymentMethodTokens
{
    /// <summary>Always succeeds.</summary>
    public const string Success = "tok_success";

    /// <summary>Always declined by the issuing bank.</summary>
    public const string Declined = "tok_declined";

    /// <summary>Declined for lack of funds - a different reason, same outcome.</summary>
    public const string InsufficientFunds = "tok_insufficient_funds";

    /// <summary>
    /// Hangs for longer than any sensible caller will wait.
    ///
    /// This one is the most valuable of the four. It lets you reproduce, on
    /// demand, the worst failure in a distributed system: the caller times out
    /// while the work SUCCEEDS anyway. The money moves, the answer is lost, and
    /// the order is left saying PaymentProcessing forever.
    ///
    /// You cannot reason about that failure until you have watched it happen.
    /// </summary>
    public const string Timeout = "tok_timeout";

    public static bool IsKnown(string token) =>
        token is Success or Declined or InsufficientFunds or Timeout;
}
