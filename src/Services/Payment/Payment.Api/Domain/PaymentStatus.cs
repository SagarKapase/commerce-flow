namespace Payment.Api.Domain;

/// <summary>
/// The life of a payment.
///
///   Pending ──succeed──> Succeeded   (money moved)
///      └─────fail───────> Failed     (it did not)
///
/// Both outcomes are terminal. There is no path from Failed back to Pending:
/// retrying is a NEW payment attempt, not a resurrection of the old one, and
/// keeping the failed record is how you can later answer "how many cards did
/// this customer try?"
///
/// WHY Pending EXISTS AT ALL when our simulation answers instantly:
/// because real gateways do not. A card payment can bounce through 3-D Secure,
/// a bank redirect, or a webhook that arrives minutes later. Modelling the
/// in-flight state now means the shape does not have to change when the fake
/// gateway is replaced by a real one - and it is the state an order sits in
/// while it waits, which Phase 11's saga depends on.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Created, sent to the gateway, no answer yet.</summary>
    Pending = 1,

    /// <summary>The money moved. Terminal.</summary>
    Succeeded = 2,

    /// <summary>Declined, or the gateway refused. Terminal.</summary>
    Failed = 3
}
