using CommerceFlow.BuildingBlocks.Authentication;
using Catalog.Api.Authentication;
using Catalog.Api.ErrorHandling;
using Catalog.Application.Abstractions;
using Catalog.Application.Categories;
using Catalog.Application.Products;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

// =====================================================================
// COMPOSITION ROOT
//
// This file is deliberately explicit. Every registration is visible, and
// nothing is hidden behind an AddInfrastructure()-style extension method yet.
// You should be able to read this top to bottom and know exactly what the
// service does at startup. We will refactor it when it becomes hard to read -
// not before, and when we do, you will see what moved and why.
// =====================================================================

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------
// 1. MVC controllers
//
// Registers everything needed to discover [ApiController] classes, bind
// requests, run validation and serialise responses. Without this,
// app.MapControllers() below would find nothing.
// ---------------------------------------------------------------------
builder.Services.AddControllers();

// ---------------------------------------------------------------------
// 2. Swagger / OpenAPI, with a Bearer token box (Phase 3)
//
// ApiExplorer walks the routing table and describes every endpoint;
// SwaggerGen turns that description into an OpenAPI document; the UI
// renders it as a test page.
//
// The security definition is what puts the "Authorize" button in the UI.
// Without it you can see the admin-only endpoints but have no way to send
// a token to them.
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
            "Swagger adds the \"Bearer \" prefix."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() }
    });
});

// ---------------------------------------------------------------------
// 3. Database
//
// AddDbContext registers CatalogDbContext with a SCOPED lifetime: one
// instance per HTTP request. That matters - the change tracker holds the
// state of the entities loaded during this request, and sharing it across
// requests would leak one user's data into another's unit of work.
// ---------------------------------------------------------------------
builder.Services.AddDbContext<CatalogDbContext>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("Database"));
});

builder.Services.AddScoped<ICatalogDbContext>(serviceProvider =>
    serviceProvider.GetRequiredService<CatalogDbContext>());

// ---------------------------------------------------------------------
// 4. Application services
// ---------------------------------------------------------------------
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IProductService, ProductService>();

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
//
// Order is behaviour, not style. Each call wraps the ones after it, so a
// request travels down this list and the response travels back up.
// =====================================================================

// First, so it can catch exceptions thrown by everything below it.
app.UseExceptionHandler();

// The JWT handler rejects a request by setting 401 and writing NOTHING -
// an empty body with a WWW-Authenticate header. That is legal, but it is
// also the reason people think their 401 is "broken". This fills empty
// error responses with the same ProblemDetails shape (including a traceId)
// that the rest of the API returns, so failures look consistent whether
// they came from our code or from the framework.
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // Swagger is a development tool. Exposing the full API surface of a
    // production service to anonymous callers is an information leak.
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// ORDER MATTERS AND IS NOT NEGOTIABLE.
// UseAuthentication reads the Authorization header, validates the token and
// builds HttpContext.User. UseAuthorization then checks [Authorize] against
// that user. Swap them and User is still empty when the check runs, so every
// authenticated request returns 401 and it looks like the token is broken.
app.UseAuthentication();
app.UseAuthorization();

// Adds the controller endpoints to the routing table.
app.MapControllers();

app.Run();
