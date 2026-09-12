using CommerceFlow.BuildingBlocks.Messaging;

namespace Ordering.Infrastructure.Messaging;

/// <summary>
/// ============ THE PUBLIC CONTRACTS ORDERING PUBLISHES ============
///
/// These three records are an API. Not "like an API" - an API. Other services
/// deserialise them, and once one does, renaming a property here breaks it
/// silently, at runtime, in a process you are not looking at.
///
/// Rules that follow from that:
///
///   IDS AND PRIMITIVES ONLY. No Order, no OrderItem, no Address. Publishing
///   an entity exports your internal model as a contract, and then you cannot
///   refactor it. A consumer that needs more detail should call back and ask -
///   which also means it gets the CURRENT state rather than a stale copy.
///
///   PAST TENSE, ALWAYS. "OrderPlaced", not "PlaceOrder". An event is a
///   statement of fact about something that already happened and cannot be
///   refused. A COMMAND is an instruction to one specific handler that may
///   fail - different thing, different naming, usually a different exchange.
///   Getting this backwards produces systems where "events" are really
///   commands in disguise and the publisher secretly depends on the consumer.
///
///   ADDITIVE CHANGES ONLY. Adding an optional field is safe - a consumer's
///   tolerant reader ignores what it does not know. Removing or renaming one
///   is a breaking change that needs a new event type
///   (OrderPlacedV2IntegrationEvent) published alongside the old one until
///   every consumer has moved.
///
/// And notice what is NOT shared: Notification.Worker declares its own copies
/// of these. See the comment in MessagingServiceCollectionExtensions for why
/// a shared contracts package trades the coupling you removed back in.
/// =================================================================
/// </summary>
public sealed record OrderPlacedIntegrationEvent : IntegrationEvent
{
    public override string RoutingKey => "order.placed";

    public required Guid OrderId { get; init; }

    public required Guid CustomerId { get; init; }

    public required decimal TotalAmount { get; init; }

    public required int ItemCount { get; init; }

    /// <summary>
    /// The hold Inventory is keeping. A consumer that needs to release it -
    /// the saga in Phase 12 - needs this, and asking Ordering for it later
    /// would be a network call to learn something the event could have carried.
    /// </summary>
    public required Guid? InventoryReservationId { get; init; }
}

public sealed record OrderConfirmedIntegrationEvent : IntegrationEvent
{
    public override string RoutingKey => "order.confirmed";

    public required Guid OrderId { get; init; }

    public required Guid CustomerId { get; init; }

    public required decimal TotalAmount { get; init; }

    public required Guid? PaymentId { get; init; }
}

public sealed record OrderPaymentFailedIntegrationEvent : IntegrationEvent
{
    public override string RoutingKey => "order.payment-failed";

    public required Guid OrderId { get; init; }

    public required Guid CustomerId { get; init; }

    public required decimal TotalAmount { get; init; }

    /// <summary>Written for a customer to read, because one will.</summary>
    public required string? FailureReason { get; init; }
}
