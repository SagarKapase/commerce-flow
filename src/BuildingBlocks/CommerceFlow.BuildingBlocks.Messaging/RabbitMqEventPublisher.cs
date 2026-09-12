using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace CommerceFlow.BuildingBlocks.Messaging;

public sealed class RabbitMqEventPublisher : IEventPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqEventPublisher> _logger;

    // One channel, reused. Creating a channel per publish is cheap but not
    // free, and it would churn the broker's channel table under load.
    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private IChannel? _channel;

    public RabbitMqEventPublisher(
        RabbitMqConnectionProvider connectionProvider,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqEventPublisher> logger)
    {
        _connectionProvider = connectionProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IntegrationEvent
    {
        try
        {
            var channel = await GetChannelAsync(cancellationToken);

            var body = JsonSerializer.SerializeToUtf8Bytes(
                integrationEvent, integrationEvent.GetType(), SerializerOptions);

            var properties = new BasicProperties
            {
                // ================================================
                // PERSISTENT. Without this the broker keeps the message in
                // memory only, and a RabbitMQ restart loses it - even from a
                // durable queue. Durability is TWO settings that people
                // routinely mix up:
                //
                //   a DURABLE QUEUE survives a broker restart as a queue
                //   a PERSISTENT MESSAGE survives a broker restart as a message
                //
                // You need both. A persistent message in a non-durable queue
                // dies with the queue; a transient message in a durable queue
                // dies on its own.
                // ================================================
                Persistent = true,

                // The deduplication key a consumer will use in Phase 14. Note
                // it is the EVENT's id, generated once when the event was
                // created - so a redelivery of the same message carries the
                // same value, which is exactly the property dedup needs.
                MessageId = integrationEvent.EventId.ToString(),

                // How a consumer knows what it is looking at without guessing
                // from the routing key or sniffing the JSON.
                Type = integrationEvent.GetType().Name,

                ContentType = "application/json",

                // Seconds since the epoch. Useful in the management UI when
                // you are staring at a message in a DLQ wondering how old it is.
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            };

            // ====================================================
            // PUBLISHER CONFIRMS.
            //
            // The channel was created with confirmations enabled, so this
            // await does NOT return when the bytes hit the socket. It returns
            // when the BROKER has acknowledged that it has the message and, for
            // a persistent message, has written it to disk.
            //
            // Without confirms, BasicPublish is fire-and-forget: it succeeds
            // even if the broker is in the middle of falling over, and the
            // message evaporates with nobody the wiser.
            //
            // Note what confirms still do NOT protect against, because this is
            // the gap Phase 13 exists to close: if this service commits an
            // order to its database and then crashes BEFORE this line runs, the
            // order exists and the event never will. No amount of confirming
            // fixes that, because the problem is that the database write and
            // the publish are two separate operations with no transaction
            // around them.
            // ====================================================
            await channel.BasicPublishAsync(
                exchange: _options.ExchangeName,
                routingKey: integrationEvent.RoutingKey,

                // mandatory: false. True would make the broker return the
                // message if no queue is bound to match the routing key.
                // We want false: publishing an event nobody subscribes to yet
                // is normal and correct - the publisher should not know or
                // care who is listening. That decoupling is the entire point.
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Published {EventType} {EventId} to {Exchange} with routing key {RoutingKey}",
                properties.Type,
                integrationEvent.EventId,
                _options.ExchangeName,
                integrationEvent.RoutingKey);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Failed to publish {EventType} {EventId}",
                integrationEvent.GetType().Name,
                integrationEvent.EventId);

            throw new EventPublishFailedException(
                $"Could not publish {integrationEvent.GetType().Name}.", exception);
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await _channelLock.WaitAsync(cancellationToken);

        try
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);

            _channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true),
                cancellationToken);

            // Declaring is IDEMPOTENT: if the exchange already exists with the
            // same settings this does nothing, and if it does not exist it is
            // created. So every publisher and every consumer declares what it
            // needs, and no separate "set up the broker" step has to run first.
            //
            // The catch: declaring an existing exchange with DIFFERENT settings
            // fails with PRECONDITION_FAILED and kills the channel. That is a
            // feature - it stops two services quietly disagreeing about whether
            // a queue is durable - but it is also why changing topology on a
            // live system needs care.
            await _channel.ExchangeDeclareAsync(
                exchange: _options.ExchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);

            return _channel;
        }
        finally
        {
            _channelLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync();
            await _channel.DisposeAsync();
        }

        _channelLock.Dispose();
    }
}
