using Inventory.Domain.Exceptions;

namespace Inventory.Domain.Entities;

/// <summary>
/// A hold placed across one or more products.
///
/// WHY ONE RESERVATION COVERS MANY PRODUCTS:
/// An order has several lines and needs ALL of them or none. If each line were
/// its own reservation, the caller would make N calls, and the third one
/// failing would leave two orphaned holds that somebody has to remember to
/// clean up. Making the reservation the unit means Inventory can guarantee
/// all-or-nothing inside a single database transaction - and a guarantee the
/// service makes for itself is worth ten the caller has to implement.
///
/// This is also a rehearsal for Phase 7: Order will have exactly this shape -
/// a root with lines, where the rules live on the root.
/// </summary>
public sealed class InventoryReservation
{
    private readonly List<ReservationLine> _lines = new();

    private InventoryReservation()
    {
    }

    private InventoryReservation(Guid id, Guid referenceId, DateTime createdAtUtc)
    {
        Id = id;
        ReferenceId = referenceId;
        Status = ReservationStatus.Held;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// The caller's own identifier for whatever this hold is for.
    ///
    /// It is deliberately NOT called OrderId. Inventory does not know what an
    /// order is, and should not - it manages stock, and something outside asked
    /// it to set some aside. In Phase 8 the Ordering service will pass its order
    /// id here; if a warehouse tool ever needed a hold, it would pass its own
    /// reference and nothing in this class would change.
    ///
    /// It also gives Phase 13 something to make idempotent: "if I already have
    /// a reservation for this reference, do not create a second one".
    /// </summary>
    public Guid ReferenceId { get; private set; }

    public ReservationStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public IReadOnlyCollection<ReservationLine> Lines => _lines.AsReadOnly();

    public static InventoryReservation Create(
        Guid referenceId,
        IReadOnlyCollection<(Guid ProductId, int Quantity)> lines)
    {
        if (lines.Count == 0)
        {
            throw new ArgumentException("A reservation must contain at least one line.", nameof(lines));
        }

        var duplicateProducts = lines
            .GroupBy(line => line.ProductId)
            .Any(group => group.Count() > 1);

        if (duplicateProducts)
        {
            // The composite key would reject this at the database anyway; doing
            // it here turns a provider-specific constraint error into a clear
            // message about what the caller did wrong.
            throw new ArgumentException(
                "A reservation cannot list the same product twice.", nameof(lines));
        }

        var reservation = new InventoryReservation(
            Guid.CreateVersion7(),
            referenceId,
            DateTime.UtcNow);

        foreach (var (productId, quantity) in lines)
        {
            if (quantity < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lines), quantity, "Reservation quantities must be at least 1.");
            }

            reservation._lines.Add(new ReservationLine(reservation.Id, productId, quantity));
        }

        return reservation;
    }

    /// <summary>
    /// Payment succeeded - the hold becomes a sale.
    /// </summary>
    /// <returns>
    /// <c>true</c> if this call changed anything; <c>false</c> if it was
    /// already Confirmed.
    ///
    /// RETURNING false RATHER THAN THROWING IS DELIBERATE. Confirming twice
    /// must be safe: in Phase 10 a message broker will deliver "payment
    /// succeeded" at least once, which in practice means sometimes twice, and
    /// the second delivery must not be an error. This is what "naturally
    /// idempotent" looks like when you design for it instead of bolting on a
    /// deduplication table.
    ///
    /// The caller uses the bool to decide whether to touch stock - because
    /// applying the stock change twice is exactly the bug this prevents.
    /// </returns>
    public bool Confirm()
    {
        if (Status == ReservationStatus.Confirmed)
        {
            return false;
        }

        if (Status == ReservationStatus.Released)
        {
            // This one DOES throw. Released is not "not yet confirmed", it is
            // a decision that went the other way, and quietly overriding it
            // would ship goods for an order somebody cancelled.
            throw new InvalidReservationStateException(Id, Status, "confirmed");
        }

        Status = ReservationStatus.Confirmed;
        CompletedAtUtc = DateTime.UtcNow;

        return true;
    }

    /// <summary>
    /// The order failed - give the units back.
    /// </summary>
    /// <returns>
    /// <c>true</c> if this call changed anything; <c>false</c> if it was
    /// already Released.
    ///
    /// Idempotent for an even better reason than Confirm: this is the
    /// COMPENSATING action of a saga. Compensation runs when things have
    /// already gone wrong, so it will be retried, and a retry that fails is a
    /// saga that cannot finish unwinding.
    /// </returns>
    public bool Release()
    {
        if (Status == ReservationStatus.Released)
        {
            return false;
        }

        if (Status == ReservationStatus.Confirmed)
        {
            // The goods have shipped. Returning them is a refund, which is a
            // different business process with different paperwork - not
            // something to do by quietly moving numbers between two columns.
            throw new InvalidReservationStateException(Id, Status, "released");
        }

        Status = ReservationStatus.Released;
        CompletedAtUtc = DateTime.UtcNow;

        return true;
    }
}
