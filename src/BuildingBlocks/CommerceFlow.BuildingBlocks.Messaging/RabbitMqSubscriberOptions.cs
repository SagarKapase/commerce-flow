namespace CommerceFlow.BuildingBlocks.Messaging;

/// <summary>
/// What one subscribing service wants from the broker.
/// </summary>
public sealed class RabbitMqSubscriberOptions
{
    /// <summary>
    /// The queue this service reads from.
    ///
    /// THE QUEUE BELONGS TO THE CONSUMER, NOT THE PUBLISHER. Ordering
    /// publishes to an exchange and has no idea this queue exists. Notification
    /// creates it, binds it to the patterns it cares about, and owns its
    /// backlog. Add a second subscriber tomorrow and it declares its own queue
    /// with its own bindings; the publisher does not change, does not redeploy,
    /// and is not told.
    ///
    /// That is what people mean by "decoupled", concretely.
    /// </summary>
    public required string QueueName { get; init; }

    /// <summary>
    /// The routing patterns to bind. "order.#" means every order event; "#"
    /// means everything; "order.placed" means exactly that one.
    ///
    /// # matches zero or more dot-separated words. * matches exactly one.
    /// So for "order.payment.failed": "order.#" matches, "order.*" does not.
    /// </summary>
    public required IReadOnlyList<string> RoutingKeys { get; init; }

    /// <summary>
    /// How many unacknowledged messages the broker will hand this consumer at
    /// once.
    ///
    /// WITHOUT THIS (the default is unlimited) the broker pushes the entire
    /// queue at a single consumer the instant it connects. Two things break:
    /// that consumer holds ten thousand messages in memory, and every OTHER
    /// consumer of the same queue sits idle with nothing to do. You scaled
    /// out and got no throughput.
    ///
    /// A small prefetch is how work actually spreads across consumers: each
    /// takes a few, and whoever finishes first gets the next. 10 is a
    /// reasonable default for handlers doing I/O; raise it for very fast
    /// handlers, lower it to 1 when messages are slow and you want strict
    /// fairness.
    /// </summary>
    public ushort PrefetchCount { get; init; } = 10;
}
