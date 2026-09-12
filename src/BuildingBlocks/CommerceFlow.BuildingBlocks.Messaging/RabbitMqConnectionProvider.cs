using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace CommerceFlow.BuildingBlocks.Messaging;

/// <summary>
/// Owns the single TCP connection this process has to the broker.
///
/// ============ ONE CONNECTION, MANY CHANNELS ============
/// This is the piece of RabbitMQ everybody gets wrong first, so it is worth
/// being precise:
///
///   A CONNECTION is a real TCP connection. Opening one costs a TCP
///   handshake, a TLS handshake and an AMQP handshake - tens of milliseconds,
///   and a file handle and some memory on the broker for as long as it lives.
///   You want ONE per process, held open for the life of the application.
///
///   A CHANNEL is a lightweight virtual session multiplexed over that one
///   connection. Creating one is essentially free. All the actual work -
///   declaring, publishing, consuming, acknowledging - happens on a channel.
///
/// The mistake is treating a connection like an HttpClient request: open,
/// publish, dispose. Do that per message and a busy service spends more time
/// handshaking than working, and the broker runs out of file descriptors.
///
/// The OTHER mistake is sharing one channel across threads. A channel is NOT
/// thread-safe, and concurrent publishes on one will corrupt the protocol
/// framing in ways that produce baffling errors. One channel per publisher,
/// one per consumer.
///
/// Registered as a SINGLETON, which is what makes "one connection" true.
/// ========================================================
/// </summary>
public sealed class RabbitMqConnectionProvider : IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private IConnection? _connection;

    public RabbitMqConnectionProvider(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConnectionProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns the shared connection, opening it on first use.
    ///
    /// Lazy rather than eager, on purpose: a service should still START when
    /// the broker is down. It will fail to publish, and that failure is
    /// handled where it matters - but refusing to boot at all would mean a
    /// broker restart takes every service with it.
    /// </summary>
    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        // Two requests arriving together must not open two connections. The
        // lock is held only across the handshake, and the fast path above
        // never touches it.
        await _connectionLock.WaitAsync(cancellationToken);

        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                VirtualHost = _options.VirtualHost,
                UserName = _options.UserName,
                Password = _options.Password,

                // The client library reconnects on its own when the broker
                // bounces or the network blips, and re-declares the topology
                // and consumers it had. Without this, one restart of RabbitMQ
                // silently stops every consumer in the system until somebody
                // notices and restarts the services.
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),

                // Sends a heartbeat both ways. Without it, a connection killed
                // by a firewall or a sleeping laptop looks perfectly healthy
                // from this side until the first publish fails minutes later.
                RequestedHeartbeat = TimeSpan.FromSeconds(30)
            };

            // The client-provided name shows up in the management UI's
            // Connections tab. Worth setting: "Ordering.Api" tells you which
            // service is misbehaving, "192.168.1.5:54021" does not.
            var connectionName = $"{AppDomain.CurrentDomain.FriendlyName}";

            _connection = await factory.CreateConnectionAsync(connectionName, cancellationToken);

            _logger.LogInformation(
                "Connected to RabbitMQ at {Host}:{Port} vhost {VirtualHost} as {ClientName}",
                _options.HostName,
                _options.Port,
                _options.VirtualHost,
                connectionName);

            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            // CloseAsync before DisposeAsync sends a proper AMQP close frame,
            // so the broker knows this was deliberate rather than a client
            // that fell over. It matters in the logs when you are trying to
            // work out whether something crashed.
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }

        _connectionLock.Dispose();
    }
}
