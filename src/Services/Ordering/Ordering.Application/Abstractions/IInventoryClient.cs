namespace Ordering.Application.Abstractions;

/// <summary>A product and quantity to hold.</summary>
public sealed record StockReservationLine(Guid ProductId, int Quantity);

/// <summary>
/// What happened when we asked Inventory to hold stock.
///
/// ============ EXCEPTIONS VERSUS RESULTS - THE REAL CASE ============
/// Phase 1 promised we would revisit this when a genuine example appeared.
/// Here it is, and the shape of the answer is: use both, for different things.
///
/// There are THREE possible outcomes, and they are not the same kind of thing:
///
///   1. Stock reserved.            An expected answer.
///   2. Not enough stock.          Also an expected answer. It is not an
///                                 error - it is Inventory doing its job and
///                                 telling us no, and the customer needs to be
///                                 told why.
///   3. Inventory is unreachable.  NOT an answer at all. We do not know what
///                                 the stock position is, and cannot act on it.
///
/// Outcomes 1 and 2 are both NORMAL, both need data attached, and the caller
/// must handle both - so they are a RESULT: this record, with two factories.
/// Outcome 3 is exceptional and there is nothing sensible for the caller to do
/// locally, so it is an EXCEPTION that unwinds to the edge.
///
/// The rule worth taking away: a result type for outcomes the caller is
/// expected to branch on; an exception for the ones that mean the question
/// could not be answered. Using exceptions for "insufficient stock" would make
/// an ordinary business answer look like a fault; using a result for "the
/// network is down" would make every call site carry an error check it cannot
/// meaningfully act on.
/// ===================================================================
/// </summary>
public sealed record StockReservationResult
{
    private StockReservationResult(bool reserved, Guid? reservationId, string? failureReason)
    {
        Reserved = reserved;
        ReservationId = reservationId;
        FailureReason = failureReason;
    }

    public bool Reserved { get; }

    /// <summary>Set only when <see cref="Reserved"/> is true.</summary>
    public Guid? ReservationId { get; }

    /// <summary>Set only when <see cref="Reserved"/> is false. Shown to the customer.</summary>
    public string? FailureReason { get; }

    public static StockReservationResult Succeeded(Guid reservationId) =>
        new(true, reservationId, null);

    public static StockReservationResult Refused(string reason) =>
        new(false, null, reason);
}

/// <summary>Ordering's view of the Inventory service.</summary>
public interface IInventoryClient
{
    /// <summary>
    /// Asks Inventory to hold stock for an order - all lines or none.
    /// </summary>
    /// <param name="orderId">
    /// Passed as Inventory's ReferenceId. Using the order id means a reservation
    /// can always be traced back to what it is for, and gives Phase 13 the key
    /// it needs to make this call idempotent: "do I already have a reservation
    /// for this order?"
    /// </param>
    /// <exception cref="Exceptions.DownstreamUnavailableException">
    /// Inventory could not be reached. Note what this means at the call site:
    /// we do not know whether the stock was reserved or not. The request may
    /// have arrived and succeeded with the response lost on the way back.
    /// </exception>
    Task<StockReservationResult> ReserveAsync(
        Guid orderId,
        IReadOnlyCollection<StockReservationLine> lines,
        CancellationToken cancellationToken);

    /// <summary>
    /// Turns a hold into a sale. Called after the money has moved.
    ///
    /// Safe to call twice - Inventory returns 200 and changes nothing the
    /// second time, because InventoryReservation.Confirm was written to be
    /// idempotent back in Phase 6. That was not a guess: this is the call it
    /// was written for.
    /// </summary>
    Task ConfirmAsync(Guid reservationId, CancellationToken cancellationToken);

    /// <summary>
    /// Puts held stock back on the shelf.
    ///
    /// ============ THIS IS A COMPENSATING ACTION ============
    /// It is the "undo" half of a distributed transaction that has no undo.
    /// There is no ROLLBACK across three databases, so when payment fails the
    /// only way to restore the world is to perform a SECOND business
    /// operation that reverses the first.
    ///
    /// Compensation is not a rollback, and the difference matters:
    ///
    ///   A rollback erases history. Nobody can tell it happened.
    ///   A compensation ADDS to history. The reservation row still exists,
    ///   now marked Released, and the order still exists, now marked
    ///   PaymentFailed. Both facts stay true forever - which is what a
    ///   business actually wants, because "why did this fail?" is a question
    ///   somebody will ask.
    ///
    /// It runs when things have ALREADY gone wrong, which is precisely when
    /// retries happen - so it is idempotent by design. A compensating action
    /// that fails on a retry is a saga that can never finish unwinding.
    /// =======================================================
    /// </summary>
    Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken);
}
