using CommerceFlow.BuildingBlocks.Authentication;
using CommerceFlow.BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Ordering.Api.ErrorHandling;
using Ordering.Api.Http;
using Ordering.Application.Abstractions;
using Ordering.Application.Orders;
using Ordering.Infrastructure.Http;
using Ordering.Infrastructure.Messaging;
using Ordering.Infrastructure.Persistence;
using Ordering.Infrastructure.Persistence.Repositories;

// The names are both the options key and the configuration section, so
// "Services:Basket" and the client registration can never drift apart.
const string BasketClientName = "Basket";
const string InventoryClientName = "Inventory";
const string PaymentClientName = "Payment";

// =====================================================================
// COMPOSITION ROOT
//
// Shorter than Catalog's, and the difference is worth noticing: the forty
// lines of token validation that used to sit in every service are now one
// call to a BuildingBlock. Everything else here is still explicit.
// =====================================================================

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------
// 1. MVC controllers
// ---------------------------------------------------------------------
builder.Services.AddControllers();

// ---------------------------------------------------------------------
// 2. Swagger, with a Bearer token box
// ---------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description =
            "Paste ONLY the access token from Identity's POST /api/auth/login. " +
            "The orders you can see belong to whoever that token identifies."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() }
    });
});

// ---------------------------------------------------------------------
// 3. Database
// ---------------------------------------------------------------------
builder.Services.AddDbContext<OrderingDbContext>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("Database"));
});

// ---------------------------------------------------------------------
// 4. Persistence and application services
//
// NOTE THE SHAPE OF THIS REGISTRATION, and compare it with Catalog's.
//
// Catalog registered ICatalogDbContext -> CatalogDbContext, so its application
// layer received EF Core's own abstraction. Ordering registers
// IOrderRepository -> OrderRepository: the application layer receives an
// interface IT defined, and never learns that EF Core is involved at all.
//
// Open Ordering.Application.csproj to see the consequence - it has exactly one
// package reference, and it is not EF Core.
// ---------------------------------------------------------------------
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderService, OrderService>();

// ---------------------------------------------------------------------
// 4a. Messaging (Phase 11)
//
// AddCommerceFlowMessaging brings the connection provider and the
// RabbitMQ publisher. OrderEventPublisher adapts it to the domain-shaped
// interface Ordering.Application declares, which is what keeps
// RabbitMQ.Client out of that project entirely.
// ---------------------------------------------------------------------
builder.Services.AddCommerceFlowMessaging(builder.Configuration);
builder.Services.AddScoped<IOrderEventPublisher, OrderEventPublisher>();

// ---------------------------------------------------------------------
// 4b. The two downstream services (Phase 8)
//
// NAMED OPTIONS. Basket and Inventory have the same settings shape, so one
// ServiceEndpointOptions type is bound twice under different names rather
// than two near-identical classes. Retrieve one with
// IOptionsMonitor<T>.Get("Basket").
// ---------------------------------------------------------------------
builder.Services.AddOptions<ServiceEndpointOptions>(BasketClientName)
    .Bind(builder.Configuration.GetSection($"Services:{BasketClientName}"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<ServiceEndpointOptions>(InventoryClientName)
    .Bind(builder.Configuration.GetSection($"Services:{InventoryClientName}"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<ServiceEndpointOptions>(PaymentClientName)
    .Bind(builder.Configuration.GetSection($"Services:{PaymentClientName}"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// IHttpContextAccessor lets the forwarding handler read the INCOMING
// request's Authorization header. Required for token forwarding, and the
// only reason Ordering.Api knows HttpContext exists outside a controller.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<AccessTokenForwardingHandler>();

AddDownstreamClient<IBasketClient, BasketClient>(BasketClientName);
AddDownstreamClient<IInventoryClient, InventoryClient>(InventoryClientName);
AddDownstreamClient<IPaymentClient, PaymentClient>(PaymentClientName);

// ---------------------------------------------------------------------
// 5. Authentication and authorization
//
// The first service written AFTER the BuildingBlocks extraction, so this is
// the only version of it that has ever existed here. If you want to see what
// it replaced, look at the git history of any other service's Program.cs.
// ---------------------------------------------------------------------
builder.Services.AddCommerceFlowJwtAuthentication(builder.Configuration);

// ---------------------------------------------------------------------
// 6. Error handling
// ---------------------------------------------------------------------
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// =====================================================================
// One registration shape for every downstream service.
//
// A local function rather than a copy-paste per client: the base address
// rule, the timeout, the token forwarding and the development certificate
// exception are the same for all of them, and a second copy is how one of
// them quietly ends up without a timeout.
//
// This is NOT a BuildingBlock. It is four lines of wiring specific to this
// service's Program.cs - shared code needs two services that already have
// it, and Ordering is the only one talking to two dependencies.
// =====================================================================
void AddDownstreamClient<TClient, TImplementation>(string name)
    where TClient : class
    where TImplementation : class, TClient
{
    var clientBuilder = builder.Services
        .AddHttpClient<TClient, TImplementation>((serviceProvider, httpClient) =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptionsMonitor<ServiceEndpointOptions>>()
                .Get(name);

            // Trailing slash: Uri combination replaces the last path segment,
            // so a base without one silently drops any prefix it had.
            httpClient.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        })
        // THE HANDLER PIPELINE. Every request this client makes now carries
        // the caller's bearer token, and no client method mentions
        // authentication at all.
        .AddHttpMessageHandler<AccessTokenForwardingHandler>();

    if (builder.Environment.IsDevelopment())
    {
        // Development only, and the property name says why. The downstream
        // services use the self-signed ASP.NET Core development
        // certificate; unless it has been trusted on this machine
        // (`dotnet dev-certs https --trust`), the TLS handshake fails before
        // the request is sent. In production, services present real
        // certificates and this block does not exist.
        clientBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        });
    }
}
