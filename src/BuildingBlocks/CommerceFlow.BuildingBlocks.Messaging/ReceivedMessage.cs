namespace CommerceFlow.BuildingBlocks.Messaging;

/// <summary>
/// One message off a queue, unwrapped for a handler.
///
/// Deliberately NOT deserialised into a typed event here. The BuildingBlock
/// knows about RabbitMQ; it does not know about orders, payments or baskets,
/// and it must not - the moment a shared messaging library knows the shape of
/// a business event, every service has to be redeployed together whenever that
/// event changes.
///
/// So the plumbing hands the consuming service a message type and some JSON,
/// and the service decides what it is and how to read it. That is the same
/// line drawn in Phase 7 for authentication: MECHANISM is shared, CONTRACTS
/// are not.
/// </summary>
/// <param name="MessageId">
/// The publisher's EventId. The deduplication key for Phase 14.
/// </param>
/// <param name="MessageType">
/// The event's type name, e.g. "OrderPlacedIntegrationEvent".
/// </param>
/// <param name="RoutingKey">The key it was published with, e.g. "order.placed".</param>
/// <param name="Json">The body.</param>
/// <param name="DeliveryCount">
/// How many times the broker has delivered this message. 1 on the first
/// attempt. Anything higher means a previous attempt failed or the consumer
/// died holding it - which is a useful signal that a message may be poison.
/// </param>
public sealed record ReceivedMessage(
    string MessageId,
    string MessageType,
    string RoutingKey,
    string Json,
    long DeliveryCount);
