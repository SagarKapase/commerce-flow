using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CommerceFlow.BuildingBlocks.Messaging;

/// <summary>
/// Handles one message. Implemented by the consuming service.
/// </summary>
public interface IMessageHandler
{
    /// <summary>
    /// Process the message.
    ///
    /// RETURNING NORMALLY MEANS "done, acknowledge it".
    /// THROWING MEANS "I could not, dead-letter it".
    ///
    /// There is no third option on purpose. A handler that swallows its own
    /// exceptions and returns is telling the broker the work succeeded, and
    /// the message is gone forever with nothing to show for it.
    /// </summary>
    Task HandleAsync(ReceivedMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// A long-running consumer. Connects, declares its own topology, and pumps
/// messages into an IMessageHandler.
///
/// It is a BackgroundService, which means it has no HTTP endpoint, no port and
/// no controller. Hosting one of these IS a microservice - see
/// Notification.Worker, whose entire job is to run this.
/// </summary>
public sealed class RabbitMqSubscriber : BackgroundService
{
    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly RabbitMqOptions _options;
    private readonly RabbitMqSubscriberOptions _subscriberOptions;
    private readonly IMessageHandler _handler;
    private readonly ILogger<RabbitMqSubscriber> _logger;

    private IChannel? _channel;

    public RabbitMqSubscriber(
        RabbitMqConnectionProvider connectionProvider,
        IOptions<RabbitMqOptions> options,
        IOptions<RabbitMqSubscriberOptions> subscriberOptions,
        IMessageHandler handler,
        ILogger<RabbitMqSubscriber> logger)
    {
        _connectionProvider = connectionProvider;
        _options = options.Value;
        _subscriberOptions = subscriberOptions.Value;
        _handler = handler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Retry the initial connect forever rather than crashing. A consumer
        // that exits because the broker was not up yet is a consumer somebody
        // has to restart by hand, and start-up ordering between a broker and
        // six services is not something to rely on.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(stoppingToken);
                break;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(
                    exception,
                    "Could not start consuming from {Queue}; retrying in 5 seconds",
                    _subscriberOptions.QueueName);

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task StartConsumingAsync(CancellationToken stoppingToken)
    {
        var connection = await _connectionProvider.GetConnectionAsync(stoppingToken);

        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // =============================================================
        // DECLARE THE TOPOLOGY FROM CODE.
        //
        // Nothing is created by hand in the management UI. Every exchange,
        // queue and binding this service needs is declared here, at start-up,
        // idempotently - so a brand-new broker, a wiped broker or a broker in
        // a different environment all end up correct with no runbook.
        //
        // The order matters: exchanges before the queues that bind to them.
        // =============================================================

        // The main topic exchange. Publishers declare it too; declaring twice
        // with identical settings is a no-op.
        await _channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);

        // The dead-letter exchange. DIRECT, not topic: a rejected message is
        // routed by an exact queue name, not matched against a pattern.
        await _channel.ExchangeDeclareAsync(
            exchange: _options.DeadLetterExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);

        var deadLetterQueueName = $"{_subscriberOptions.QueueName}.dlq";

        await _channel.QueueDeclareAsync(
            queue: deadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.QueueBindAsync(
            queue: deadLetterQueueName,
            exchange: _options.DeadLetterExchangeName,
            routingKey: _subscriberOptions.QueueName,
            cancellationToken: stoppingToken);

        // The working queue.
        //
        // durable:    survives a broker restart.
        // exclusive:  false - other connections may use it. True would delete
        //             it when this connection closes, which is right for a
        //             temporary reply queue and catastrophic for a work queue.
        // autoDelete: false - it must NOT vanish when the last consumer
        //             disconnects. That is the whole reason messages published
        //             while Notification is down are still waiting when it
        //             comes back.
        await _channel.QueueDeclareAsync(
            queue: _subscriberOptions.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                // Where rejected messages go. Set on the QUEUE, not on the
                // message - so the routing decision for a failure is made by
                // whoever owns the queue, which is the service best placed to
                // know what "failed" means for it.
                ["x-dead-letter-exchange"] = _options.DeadLetterExchangeName,
                ["x-dead-letter-routing-key"] = _subscriberOptions.QueueName
            },
            cancellationToken: stoppingToken);

        foreach (var routingKey in _subscriberOptions.RoutingKeys)
        {
            await _channel.QueueBindAsync(
                queue: _subscriberOptions.QueueName,
                exchange: _options.ExchangeName,
                routingKey: routingKey,
                cancellationToken: stoppingToken);
        }

        // Fair dispatch. See RabbitMqSubscriberOptions.PrefetchCount.
        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: _subscriberOptions.PrefetchCount,
            global: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, deliverEventArgs) => OnReceivedAsync(deliverEventArgs, stoppingToken);

        await _channel.BasicConsumeAsync(
            queue: _subscriberOptions.QueueName,

            // ==========================================================
            // autoAck: FALSE. The single most important argument here.
            //
            // TRUE means the broker considers a message delivered - and
            // deletes it - the instant it puts it on the wire. If the consumer
            // then crashes, or the handler throws, or the process is killed
            // mid-work, the message is simply gone. That is "at most once"
            // delivery, and it is almost never what anybody wants.
            //
            // FALSE means the broker holds the message until we explicitly
            // acknowledge it. Crash before the ack and it is redelivered to
            // somebody. That is AT LEAST ONCE, which is the guarantee this
            // system is built on - and the reason every handler has to be
            // idempotent, because "at least once" includes "twice".
            // ==========================================================
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation(
            "Consuming {Queue} bound to {Exchange} with [{RoutingKeys}], prefetch {Prefetch}",
            _subscriberOptions.QueueName,
            _options.ExchangeName,
            string.Join(", ", _subscriberOptions.RoutingKeys),
            _subscriberOptions.PrefetchCount);
    }

    private async Task OnReceivedAsync(BasicDeliverEventArgs args, CancellationToken stoppingToken)
    {
        var message = new ReceivedMessage(
            MessageId: args.BasicProperties.MessageId ?? string.Empty,
            MessageType: args.BasicProperties.Type ?? string.Empty,
            RoutingKey: args.RoutingKey,
            Json: Encoding.UTF8.GetString(args.Body.Span),

            // x-delivery-count is maintained by the broker for quorum queues;
            // on a classic queue it is absent and Redelivered is the only
            // signal available, which tells you "this is not the first time"
            // but not how many.
            DeliveryCount: args.Redelivered ? 2 : 1);

        try
        {
            await _handler.HandleAsync(message, stoppingToken);

            // ACK: "I am done with this, delete it."
            //
            // AFTER the work, never before. Acknowledging first and then
            // working is how you lose messages on a crash - and it is a
            // surprisingly common mistake, because it makes the happy path
            // slightly simpler.
            await _channel!.BasicAckAsync(args.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Handler failed for {MessageType} {MessageId}; dead-lettering",
                message.MessageType,
                message.MessageId);

            // ==============================================================
            // NACK WITH requeue: FALSE.
            //
            // requeue TRUE would put the message straight back on the front of
            // the same queue, where this consumer picks it up again
            // immediately, fails again, requeues again - a hot loop that pins
            // a CPU and floods the logs while making no progress. That is a
            // POISON MESSAGE, and requeue-on-failure is how you build one.
            //
            // requeue FALSE sends it to the dead-letter exchange instead. The
            // message is preserved, out of the way, visible in the management
            // UI, and a human can look at why it failed and replay it once the
            // bug is fixed. The queue keeps moving.
            //
            // The middle ground - retry a few times with a delay before giving
            // up - needs a retry queue with a TTL that dead-letters back to the
            // main queue. Worth knowing it exists; not worth building before
            // you have a failure that would actually benefit from it.
            // ==============================================================
            await _channel!.BasicNackAsync(
                args.DeliveryTag, multiple: false, requeue: false, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Closing the channel cleanly means any message currently held but not
        // yet acknowledged is returned to the queue rather than lost - the
        // broker treats an unacked message on a closed channel as undelivered.
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
            await _channel.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
