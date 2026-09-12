using Inventory.Domain.Entities;

namespace Inventory.Domain.Exceptions;

/// <summary>
/// Thrown when a reservation is asked to make a transition its current state
/// does not allow - confirming something already released, for instance.
///
/// This is the exception type that makes a state machine real. Without it,
/// "you cannot confirm a released reservation" is a comment; with it, the
/// object refuses.
/// </summary>
public sealed class InvalidReservationStateException : Exception
{
    public InvalidReservationStateException(
        Guid reservationId,
        ReservationStatus currentStatus,
        string attemptedTransition)
        : base($"Reservation '{reservationId}' is {currentStatus} and cannot be {attemptedTransition}.")
    {
        ReservationId = reservationId;
        CurrentStatus = currentStatus;
    }

    public Guid ReservationId { get; }

    public ReservationStatus CurrentStatus { get; }
}
