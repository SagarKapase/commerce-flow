using Ordering.Domain.Entities;

namespace Ordering.Domain.Exceptions;

/// <summary>
/// Thrown when an order is asked to make a transition its current state does
/// not allow.
///
/// This exception is what turns the diagram in OrderStatus from documentation
/// into behaviour. Without it, "you cannot confirm a cancelled order" is a
/// comment somebody will contradict in six months. With it, the object refuses,
/// loudly, and names both the state it was in and the thing that was attempted.
///
/// Both of those matter in the message. "Invalid state transition" tells a
/// debugger nothing; "Order X is Cancelled and cannot be confirmed" tells them
/// what happened and roughly why.
/// </summary>
public sealed class InvalidOrderStateTransitionException : Exception
{
    public InvalidOrderStateTransitionException(
        Guid orderId,
        OrderStatus currentStatus,
        string attemptedTransition)
        : base($"Order '{orderId}' is {currentStatus} and cannot be {attemptedTransition}.")
    {
        OrderId = orderId;
        CurrentStatus = currentStatus;
        AttemptedTransition = attemptedTransition;
    }

    public Guid OrderId { get; }

    public OrderStatus CurrentStatus { get; }

    public string AttemptedTransition { get; }
}
