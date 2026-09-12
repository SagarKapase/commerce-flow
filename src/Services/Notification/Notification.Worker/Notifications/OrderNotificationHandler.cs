using System.Text.Json;
using CommerceFlow.BuildingBlocks.Messaging;
using Notification.Worker.IntegrationEvents;

namespace Notification.Worker.Notifications;

/// <summary>
/// The only thing in this service that is not plumbing: what to DO with an
/// order event.
///
/// It "sends" notifications by writing structured log lines. No SMTP, no
/// provider, no template engine - because none of that would teach anything
/// about messaging, and all of it would need credentials and a mail server
/// before the phase could be tested.
///
/// What this file does demonstrate is the shape a real one has: dispatch on
/// message type, deserialise into a contract you own, do the work, and let an
/// exception mean "dead-letter this".
/// </summary>
public sealed class OrderNotificationHandler : IMessageHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<OrderNotificationHandler> _logger;

    public OrderNotificationHandler(ILogger<OrderNotificationHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(ReceivedMessage message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Received {MessageType} (id {MessageId}, routing key {RoutingKey}, delivery {DeliveryCount})",
            message.MessageType,
            message.MessageId,
            message.RoutingKey,
            message.DeliveryCount);

        // ==============================================================
        // AT LEAST ONCE MEANS THIS WILL SOMETIMES RUN TWICE.
        //
        // The broker guarantees delivery, not single delivery. Redelivery
        // happens whenever an ack is lost, a consumer dies mid-handler, or a
        // connection drops after the work but before the acknowledgement.
        //
        // Sending a duplicate "your order is confirmed" email is mildly
        // embarrassing, which is exactly why Notification is the RIGHT first
        // consumer to build: the cost of getting idempotency wrong here is a
        // second email, not a second charge.
        //
        // Phase 14 adds a ProcessedMessages table keyed on MessageId, and a
        // consumer checks it inside the same transaction as its work. Doing
        // that here would be machinery without a problem to justify it - but
        // note the handler is already ready for it, because the publisher puts
        // the EventId in MessageId on every message.
        // ==============================================================

        // Dispatch on the message TYPE rather than the routing key. Both are
        // available, and either would work here - but the routing key is an
        // ADDRESS (how the broker decided this belongs in our queue) while the
        // type is the CONTENT (what the message actually is). Two different
        // events could legitimately share a routing key pattern; they can
        // never share a type.
        switch (message.MessageType)
        {
            case "OrderPlacedIntegrationEvent":
                await HandleOrderPlacedAsync(message.Json, cancellationToken);
                break;

            case "OrderConfirmedIntegrationEvent":
                await HandleOrderConfirmedAsync(message.Json, cancellationToken);
                break;

            case "OrderPaymentFailedIntegrationEvent":
                await HandlePaymentFailedAsync(message.Json, cancellationToken);
                break;

            default:
                // An event type this service does not know about.
                //
                // ACKNOWLEDGE IT, do not dead-letter it. Returning normally
                // means "handled", and for an event we do not care about that
                // is TRUE - our binding is "order.#", so a new order event
                // added by Ordering next month will arrive here whether or not
                // anybody told us. Dead-lettering it would fill a DLQ with
                // messages that are not errors, and train everybody to ignore
                // the DLQ.
                _logger.LogDebug(
                    "Ignoring {MessageType} - no handler, and that is fine",
                    message.MessageType);
                break;
        }
    }

    private Task HandleOrderPlacedAsync(string json, CancellationToken cancellationToken)
    {
        var @event = Deserialize<OrderPlacedEvent>(json);

        _logger.LogInformation(
            "EMAIL -> customer {CustomerId}: \"Thanks! We have your order {OrderId} " +
            "({ItemCount} item(s), {TotalAmount:N2}). We are getting it ready.\"",
            @event.CustomerId,
            @event.OrderId,
            @event.ItemCount,
            @event.TotalAmount);

        return Task.CompletedTask;
    }

    private Task HandleOrderConfirmedAsync(string json, CancellationToken cancellationToken)
    {
        var @event = Deserialize<OrderConfirmedEvent>(json);

        _logger.LogInformation(
            "EMAIL -> customer {CustomerId}: \"Payment received for order {OrderId}. " +
            "{TotalAmount:N2} charged. Your order is confirmed and on its way.\"",
            @event.CustomerId,
            @event.OrderId,
            @event.TotalAmount);

        return Task.CompletedTask;
    }

    private Task HandlePaymentFailedAsync(string json, CancellationToken cancellationToken)
    {
        var @event = Deserialize<OrderPaymentFailedEvent>(json);

        _logger.LogWarning(
            "EMAIL -> customer {CustomerId}: \"We could not take payment for order {OrderId}. " +
            "{FailureReason} Nothing has been charged and your items have been released.\"",
            @event.CustomerId,
            @event.OrderId,
            @event.FailureReason);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Deserialise, and THROW if the body is not what we expected.
    ///
    /// Throwing is the correct response: a message this service cannot read is
    /// a message it cannot handle, and the subscriber will dead-letter it so a
    /// human can look at the body and work out who sent it. Returning quietly
    /// would acknowledge and destroy evidence of a contract breach.
    /// </summary>
    private static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
               ?? throw new InvalidOperationException(
                   $"Message body could not be read as {typeof(T).Name}.");
    }
}
