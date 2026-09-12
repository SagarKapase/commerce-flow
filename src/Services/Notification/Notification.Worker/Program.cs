using Microsoft.Extensions.Hosting;
using CommerceFlow.BuildingBlocks.Messaging;
using Notification.Worker.Notifications;

// =====================================================================
// A MICROSERVICE WITH NO HTTP.
//
// Host.CreateApplicationBuilder, not WebApplication.CreateBuilder. There is
// no Kestrel, no port, no middleware pipeline and no MapControllers. This
// process starts, opens one connection to the broker, consumes a queue, and
// keeps doing that until it is stopped.
//
// Fourteen lines, and every one of them is registration. Everything that
// makes it work is in the BuildingBlock; everything it actually DOES is in
// OrderNotificationHandler.
// =====================================================================

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddCommerceFlowSubscriber<OrderNotificationHandler>(
    builder.Configuration,

    // OUR queue. Notification owns it, declares it, and is the only thing
    // that reads it. Ordering has never heard of it.
    queueName: "notification.order-events",

    // "order.#" - every order event, including the ones that do not exist
    // yet. When Phase 12 adds order.cancelled, this service receives it with
    // no change here and no redeploy. That is what a topic exchange is for.
    routingKeys: "order.#");

var host = builder.Build();

await host.RunAsync();
