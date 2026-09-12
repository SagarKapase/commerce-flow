namespace CommerceFlow.BuildingBlocks.Messaging;

/// <summary>
/// Sends an integration event to the broker.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes an event and waits for the broker to confirm it has it.
    /// </summary>
    /// <exception cref="EventPublishFailedException">
    /// The broker could not be reached, or refused to confirm the message.
    ///
    /// THE CALLER MUST DECIDE WHAT THIS MEANS. It is never "carry on
    /// silently": either the work depends on the event, in which case the
    /// operation failed, or it does not, in which case the failure still has
    /// to be recorded somewhere a human will see it.
    ///
    /// The third option - guaranteeing the event goes out eventually, even
    /// across a crash - is the Outbox, and it is Phase 13. Publishing directly
    /// like this can always lose a message, and the whole point of Phase 13 is
    /// that no amount of retrying here fixes that.
    /// </exception>
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IntegrationEvent;
}

/// <summary>Thrown when an event could not be handed to the broker.</summary>
public sealed class EventPublishFailedException : Exception
{
    public EventPublishFailedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
