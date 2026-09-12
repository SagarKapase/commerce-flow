using System.ComponentModel.DataAnnotations;

namespace CommerceFlow.BuildingBlocks.Messaging;

/// <summary>
/// How to reach the broker, and what topology to expect there.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required]
    public string HostName { get; init; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; init; } = 5672;

    /// <summary>
    /// A VIRTUAL HOST - RabbitMQ's own isolation boundary, and the reason this
    /// project can share one broker with anything else on the machine.
    ///
    /// A vhost is a completely separate namespace for exchanges, queues,
    /// bindings and permissions. Two vhosts on one broker cannot see each
    /// other at all: an exchange called "commerceflow.events" in one has
    /// nothing to do with an exchange of the same name in another, and a user
    /// granted access to one cannot even list the other.
    ///
    /// It is the same idea as a database on a database server. You would not
    /// put two applications' tables in one schema and prefix the names to keep
    /// them apart; you would give each its own database. Same reasoning, same
    /// solution.
    /// </summary>
    [Required]
    public string VirtualHost { get; init; } = "/";

    [Required]
    public string UserName { get; init; } = "guest";

    /// <summary>
    /// Kept in USER SECRETS, not in appsettings.json.
    ///
    ///   dotnet user-secrets set "RabbitMq:Password" "..." --project &lt;project&gt;
    ///
    /// This is the first real credential in the project - the JWT signing key
    /// in appsettings.Development.json is a string invented for the tutorial,
    /// whereas this one belongs to a broker that is actually running. User
    /// secrets live in the developer's own profile
    /// (%APPDATA%\Microsoft\UserSecrets), outside the repository, so there is
    /// no way to commit them by accident.
    ///
    /// In production this comes from a key vault or a managed identity, and
    /// the mechanism differs - but the principle is identical: the credential
    /// never lives in a file that git can see.
    /// </summary>
    [Required(ErrorMessage =
        "RabbitMq:Password is not set. Run: dotnet user-secrets set \"RabbitMq:Password\" \"<password>\"")]
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// The TOPIC exchange every integration event is published to.
    ///
    /// Why topic and not the other three types:
    ///
    ///   FANOUT   ignores the routing key and copies every message to every
    ///            bound queue. Simple, and it means Notification must receive
    ///            payment events, inventory events and everything else, then
    ///            throw away what it does not want. Filtering in the consumer
    ///            is work the broker could have done.
    ///
    ///   DIRECT   matches the routing key exactly. A subscriber wanting all
    ///            order events needs one binding per event type, and misses
    ///            any new one until somebody remembers to add a binding.
    ///
    ///   HEADERS  matches on a dictionary instead of a string. More flexible,
    ///            slower, and almost nobody needs it.
    ///
    ///   TOPIC    matches patterns over dotted keys. "order.#" says "every
    ///            order event, including ones that do not exist yet", which is
    ///            exactly what a notification service wants to say.
    /// </summary>
    [Required]
    public string ExchangeName { get; init; } = "commerceflow.events";

    /// <summary>
    /// The DEAD LETTER exchange. Messages a consumer rejects are routed here
    /// instead of being destroyed or retried forever.
    /// </summary>
    [Required]
    public string DeadLetterExchangeName { get; init; } = "commerceflow.dlx";
}
