namespace Inventory.Domain.Entities;

/// <summary>
/// One product and quantity within a reservation.
///
/// It has no behaviour of its own - a line never changes once the reservation
/// is created. Everything that can happen (confirm, release) happens to the
/// reservation as a WHOLE, which is exactly what makes InventoryReservation an
/// aggregate root and this a child entity rather than a second root.
/// </summary>
public sealed class ReservationLine
{
    private ReservationLine()
    {
    }

    internal ReservationLine(Guid reservationId, Guid productId, int quantity)
    {
        ReservationId = reservationId;
        ProductId = productId;
        Quantity = quantity;
    }

    /// <summary>Part of the composite key, and the foreign key to the reservation.</summary>
    public Guid ReservationId { get; private set; }

    /// <summary>
    /// Part of the composite key. A product can appear at most once per
    /// reservation - "reserve 2 and also 3 of the same thing" is one line of 5,
    /// and the key makes the alternative impossible.
    /// </summary>
    public Guid ProductId { get; private set; }

    public int Quantity { get; private set; }
}
