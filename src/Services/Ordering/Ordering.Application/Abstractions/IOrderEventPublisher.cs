using Ordering.Domain.Entities;

namespace Ordering.Application.Abstractions;

/// <summary>
/// Announces that something happened to an order.
///
/// ============ WHY THIS INTERFACE EXISTS AT ALL ============
/// There is already an IEventPublisher in the messaging BuildingBlock. Using
/// it directly from OrderService would be one less file - and it would put
/// RabbitMQ.Client into Ordering.Application.
///
/// That layer currently has exactly ONE package reference, and it is
/// Logging.Abstractions. Not EF Core, not HTTP, and it should not become
/// RabbitMQ either. The application layer describes what the business does;
/// which transport carries the news is Infrastructure's problem.
///
/// So the four-question test:
///   1. Problem: Application must not reference a broker client.
///   2. Solves: it says "an order was placed" and hands over an Order.
///   3. Why now: the alternative is a visible regression in a dependency
///      graph we have been careful about for eleven phases.
///   4. Simpler alternative: use IEventPublisher directly. Rejected for (3).
///
/// Note the signatures take a DOMAIN OBJECT, not an event. Building the wire
/// contract from it is Infrastructure's job, which means the event's shape -
/// a public contract with other services - is defined next to the code that
/// serialises it, not scattered through business logic.
/// ==========================================================
///
/// Every method is FIRE AND FORGET from the caller's point of view: they do
/// not throw. See OrderEventPublisher for why, and for what that costs.
/// </summary>
public interface IOrderEventPublisher
{
    Task PublishOrderPlacedAsync(Order order, CancellationToken cancellationToken);

    Task PublishOrderConfirmedAsync(Order order, CancellationToken cancellationToken);

    Task PublishOrderPaymentFailedAsync(Order order, CancellationToken cancellationToken);
}
