using CommerceFlow.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using Ordering.Application.Abstractions;
using Ordering.Domain.Entities;

namespace Ordering.Infrastructure.Messaging;

/// <summary>
/// Turns domain facts into wire contracts and hands them to the broker.
/// </summary>
public sealed class OrderEventPublisher : IOrderEventPublisher
{
    private readonly IEventPublisher _publisher;
    private readonly ILogger<OrderEventPublisher> _logger;

    public OrderEventPublisher(IEventPublisher publisher, ILogger<OrderEventPublisher> logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    public Task PublishOrderPlacedAsync(Order order, CancellationToken cancellationToken) =>
        PublishSafelyAsync(
            new OrderPlacedIntegrationEvent
            {
                OrderId = order.Id,
                CustomerId = order.CustomerId,
                TotalAmount = order.TotalAmount,
                ItemCount = order.Items.Count,
                InventoryReservationId = order.InventoryReservationId
            },
            order.Id,
            cancellationToken);

    public Task PublishOrderConfirmedAsync(Order order, CancellationToken cancellationToken) =>
        PublishSafelyAsync(
            new OrderConfirmedIntegrationEvent
            {
                OrderId = order.Id,
                CustomerId = order.CustomerId,
                TotalAmount = order.TotalAmount,
                PaymentId = order.PaymentId
            },
            order.Id,
            cancellationToken);

    public Task PublishOrderPaymentFailedAsync(Order order, CancellationToken cancellationToken) =>
        PublishSafelyAsync(
            new OrderPaymentFailedIntegrationEvent
            {
                OrderId = order.Id,
                CustomerId = order.CustomerId,
                TotalAmount = order.TotalAmount,
                FailureReason = order.FailureReason
            },
            order.Id,
            cancellationToken);

    /// <summary>
    /// ==================================================================
    /// PUBLISHING MUST NOT BREAK THE ORDER - AND THE PRICE OF THAT IS THE
    /// WHOLE ARGUMENT FOR PHASE 13.
    ///
    /// By the time any of these are called, the order is COMMITTED. The
    /// customer's stock is held, or their card has been charged. If the broker
    /// happens to be down at this instant, the only sane thing to do is log
    /// loudly and let the request succeed: failing it would return an error
    /// for work that definitely happened, and the customer would try again and
    /// buy everything twice.
    ///
    /// So this method swallows the exception. Which means:
    ///
    ///   THE EVENT IS LOST. FOREVER. SILENTLY, AS FAR AS THE CUSTOMER IS
    ///   CONCERNED.
    ///
    /// Nobody is notified. Nothing retries. The log line below is the only
    /// trace, and in Phase 12 - when the saga's next step arrives by message
    /// instead of by HTTP - a lost event will mean an order that never
    /// progresses at all.
    ///
    /// Retrying here does not fix it, and this is the part worth sitting with:
    /// the problem is not that the publish failed, it is that the database
    /// commit and the publish are TWO SEPARATE OPERATIONS with no transaction
    /// around them. Crash between them and no retry policy in the world helps,
    /// because the process that was going to retry is gone.
    ///
    /// The fix is the TRANSACTIONAL OUTBOX: write the event into the same
    /// database, in the same transaction as the order, and let a separate
    /// process publish it afterwards. One commit, two facts, atomically.
    /// That is Phase 13, and this comment is the reason it exists.
    ///
    /// Reproduce it: stop the RabbitMQ container, place an order, watch it
    /// succeed, and watch Notification never hear about it.
    /// ==================================================================
    /// </summary>
    private async Task PublishSafelyAsync<TEvent>(
        TEvent integrationEvent,
        Guid orderId,
        CancellationToken cancellationToken)
        where TEvent : IntegrationEvent
    {
        try
        {
            await _publisher.PublishAsync(integrationEvent, cancellationToken);
        }
        catch (EventPublishFailedException exception)
        {
            _logger.LogError(
                exception,
                "EVENT LOST: {EventType} for order {OrderId} could not be published. " +
                "The order itself is committed and correct, but no consumer will ever " +
                "hear about it. This is the gap the transactional outbox closes",
                typeof(TEvent).Name,
                orderId);
        }
    }
}
