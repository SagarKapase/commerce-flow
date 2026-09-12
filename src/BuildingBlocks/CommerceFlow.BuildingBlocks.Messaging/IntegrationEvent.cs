namespace CommerceFlow.BuildingBlocks.Messaging;

/// <summary>
/// The envelope every message in CommerceFlow carries.
///
/// ============ A DOMAIN EVENT IS NOT AN INTEGRATION EVENT ============
/// The word "event" gets used for two different things and conflating them
/// causes real damage:
///
///   A DOMAIN event is internal. "OrderConfirmed" raised inside the Ordering
///   service, handled inside the Ordering service, in the same transaction. It
///   can carry entities, it can change shape whenever you like, and nobody
///   outside ever sees it.
///
///   An INTEGRATION event is a PUBLIC CONTRACT. Once another service consumes
///   it you cannot rename a field without breaking them, you cannot assume
///   they received it in order, and you cannot assume they received it once.
///   It carries ids and primitives, never entities.
///
/// Everything deriving from this type is the second kind. Treat it with the
/// same care as a public API, because that is what it is.
/// ====================================================================
/// </summary>
public abstract record IntegrationEvent
{
    /// <summary>
    /// A unique id for THIS message, not for the thing it describes.
    ///
    /// Two different messages about the same order have different EventIds;
    /// the same message delivered twice has the same one. That is precisely
    /// what makes deduplication possible - and it is why Phase 14's inbox
    /// table will key on this value.
    ///
    /// Version 7 so it is time-ordered, which makes it useful for sorting a
    /// log of events as well as for identifying them.
    /// </summary>
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    /// <summary>
    /// When the thing HAPPENED, not when the message was sent or received.
    ///
    /// These drift apart the moment a message sits in a queue, and the
    /// difference matters: a consumer processing a five-minute-old
    /// "OrderPlaced" needs to know the order is five minutes old, not that it
    /// learned about it just now.
    /// </summary>
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// The routing key this event is published with, e.g. "order.placed".
    ///
    /// It lives on the event rather than at the call site so that the event
    /// type and its address can never disagree - a publisher cannot send an
    /// OrderPlaced under the routing key for a confirmation.
    ///
    /// The dotted convention is not decoration. A topic exchange splits on
    /// dots, so "order.placed" can be matched by "order.placed" exactly, by
    /// "order.*" for one more segment, or by "order.#" for any depth. Naming
    /// events &lt;aggregate&gt;.&lt;past-tense-verb&gt; means subscribers can
    /// ask for exactly the slice they want without anybody planning for it.
    /// </summary>
    public abstract string RoutingKey { get; }
}
