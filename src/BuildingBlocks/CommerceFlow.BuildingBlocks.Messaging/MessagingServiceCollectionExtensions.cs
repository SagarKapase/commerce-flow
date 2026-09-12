using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CommerceFlow.BuildingBlocks.Messaging;

/// <summary>
/// The second BuildingBlock, and it passes the same test the first one did:
/// two services already need identical code, and that code is purely technical.
///
/// Ordering publishes. Notification consumes. Both need a connection, options
/// binding, JSON handling, and the RabbitMQ API - and none of that has
/// anything to do with orders or notifications.
///
/// What is NOT in here, and never will be: the event contracts themselves.
/// OrderPlacedIntegrationEvent is declared separately in the publisher and in
/// the consumer, exactly like the HTTP DTOs in Phase 5 and Phase 8.
///
/// That surprises people, so the reasoning matters. An integration event is a
/// WIRE CONTRACT between two independently deployable services. Put it in a
/// shared package and adding one field means both services must upgrade the
/// package and redeploy together - which is the coupling microservices were
/// supposed to remove, reintroduced in the one place that guarantees it hurts.
///
/// The alternative you WILL see in the wild is a versioned contracts NuGet
/// package, and it is defensible: it gives you compile-time safety and a single
/// definition. The price is a release process that every consumer must follow.
/// Small organisation, few services, strong CI - reasonable. Many teams moving
/// at different speeds - the duplication is cheaper than the coordination.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything needed to PUBLISH events.
    /// </summary>
    public static IServiceCollection AddCommerceFlowMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // SINGLETON, and that is the whole point - see
        // RabbitMqConnectionProvider. One TCP connection per process.
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

        return services;
    }

    /// <summary>
    /// Registers everything needed to CONSUME events, and starts the consumer.
    /// </summary>
    /// <typeparam name="THandler">
    /// The service's own message handler - the only part of consuming that is
    /// not generic plumbing.
    /// </typeparam>
    public static IServiceCollection AddCommerceFlowSubscriber<THandler>(
        this IServiceCollection services,
        IConfiguration configuration,
        string queueName,
        params string[] routingKeys)
        where THandler : class, IMessageHandler
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Supplied in CODE, not bound from configuration - deliberately.
        //
        // A queue name and its bindings are part of what this service IS, not
        // how it happens to be deployed. Which events a service reacts to
        // should not be changeable by editing a config file in production, and
        // a typo in a routing key there would silently stop the service
        // receiving anything with no error anywhere.
        services.AddSingleton(Options.Create(new RabbitMqSubscriberOptions
        {
            QueueName = queueName,
            RoutingKeys = routingKeys
        }));

        services.AddSingleton<RabbitMqConnectionProvider>();

        // The handler is a singleton because the subscriber is. If a handler
        // needs a scoped dependency - a DbContext, say - it must create its own
        // scope per message, which is exactly what Phase 13's consumers will do.
        services.AddSingleton<IMessageHandler, THandler>();

        services.AddHostedService<RabbitMqSubscriber>();

        return services;
    }
}
