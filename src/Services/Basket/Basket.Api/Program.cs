using CommerceFlow.BuildingBlocks.Authentication;
using Basket.Api.Baskets;
using Basket.Api.Catalog;
using Basket.Api.ErrorHandling;
using Basket.Api.Persistence;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

// =====================================================================
// COMPOSITION ROOT
//
// One project, so this file wires up everything - domain, persistence and
// API alike. Compare it with Catalog's: the registrations are nearly the
// same, because the SHAPE of a service does not change with the number of
// assemblies you split it into.
// =====================================================================

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------
// 1. MVC controllers
// ---------------------------------------------------------------------
builder.Services.AddControllers();

// ---------------------------------------------------------------------
// 2. Swagger, with a Bearer token box
//
// Every endpoint in this service requires a token, so without the
// Authorize button Swagger would be able to do nothing at all here.
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
            "Paste ONLY the access token value from Identity's POST /api/auth/login. " +
            "Swagger adds the \"Bearer \" prefix. The basket you see belongs to whoever " +
            "that token identifies."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() }
    });
});

// ---------------------------------------------------------------------
// 3. Database
// ---------------------------------------------------------------------
builder.Services.AddDbContext<BasketDbContext>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("Database"));
});

// ---------------------------------------------------------------------
// 4. Application services
// ---------------------------------------------------------------------
builder.Services.AddScoped<IBasketService, BasketService>();

// ---------------------------------------------------------------------
// 4b. The Catalog HTTP client (Phase 5)
//
// WHY IHttpClientFactory AND NOT `new HttpClient()`:
//
//   `new HttpClient()` per request exhausts sockets. Disposing an HttpClient
//   does not release its TCP connection immediately - it sits in TIME_WAIT for
//   up to four minutes. A busy endpoint doing this runs the machine out of
//   ports and starts throwing SocketException under load, which looks like a
//   network fault and is really a `new`.
//
//   One static HttpClient fixes that and creates a subtler bug: it caches DNS
//   forever. Move the service behind a new IP and the caller keeps dialling the
//   old one until the process restarts.
//
//   The factory solves both. It pools HttpMessageHandlers and retires them on a
//   rotation (two minutes by default), so connections are reused but DNS is
//   not cached indefinitely.
//
// AddHttpClient<TInterface, TImplementation> also registers CatalogClient in DI
// as a TYPED client: it gets its own named configuration, its own handler
// pipeline, and its own log category - and you inject ICatalogClient rather
// than an HttpClient somebody has to remember to configure correctly.
// ---------------------------------------------------------------------
builder.Services.AddOptions<CatalogClientOptions>()
    .Bind(builder.Configuration.GetSection(CatalogClientOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var catalogClientBuilder = builder.Services
    .AddHttpClient<ICatalogClient, CatalogClient>((serviceProvider, httpClient) =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<CatalogClientOptions>>().Value;

        // The trailing slash is not cosmetic. Uri combination replaces the last
        // segment of the base path, so a base of "https://host/catalog"
        // (no slash) plus "api/products/1" gives "https://host/api/products/1"
        // and the "catalog" prefix vanishes. Always end a BaseAddress with "/".
        httpClient.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");

        httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    });

if (builder.Environment.IsDevelopment())
{
    // ------------------------------------------------------------------
    // DEVELOPMENT ONLY, AND THE PROPERTY IS CALLED "Dangerous" ON PURPOSE.
    //
    // Catalog serves HTTPS with the ASP.NET Core development certificate,
    // which is self-signed. Unless it has been trusted on this machine
    // (`dotnet dev-certs https --trust`), the TLS handshake fails and the
    // call dies before it starts.
    //
    // Accepting any certificate turns off the check that a server is who it
    // claims to be - exactly what TLS is for - so it is scoped to
    // Development and would be a serious finding in any other environment.
    // In production, services present certificates from a real authority,
    // and mutual TLS is how they prove their identity to each other.
    //
    // The honest alternative is to run `dotnet dev-certs https --trust` once
    // and delete this block. It is here so the phase works on a fresh
    // machine rather than failing with an SSL error nobody expects.
    // ------------------------------------------------------------------
    catalogClientBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
}

// ---------------------------------------------------------------------
// 5. Authentication and authorization
//
// PHASE 7 REFACTOR. This single call replaces the forty lines that used to
// sit here: options binding and validation, AddJwtBearer, every
// TokenValidationParameter, and AddAuthorization. The same forty lines were
// in four services; they now live once, in
// src/BuildingBlocks/CommerceFlow.BuildingBlocks.Authentication.
//
// The comments moved with the code - nothing was lost, only relocated. Open
// AuthenticationServiceCollectionExtensions.cs the first time authentication
// misbehaves and every decision is still explained there.
//
// It is still LOCAL validation: no call to Identity, from here or anywhere.
// ---------------------------------------------------------------------
builder.Services.AddCommerceFlowJwtAuthentication(builder.Configuration);

// ---------------------------------------------------------------------
// 6. Error handling
// ---------------------------------------------------------------------
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

// =====================================================================
// MIDDLEWARE PIPELINE
// =====================================================================

app.UseExceptionHandler();

// Gives the framework's own empty 401/403/404 responses the same
// ProblemDetails body the rest of the API returns.
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Authentication builds HttpContext.User; authorization then checks
// [Authorize] against it. This order is not negotiable.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
