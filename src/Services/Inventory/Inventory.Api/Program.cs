using CommerceFlow.BuildingBlocks.Authentication;
using Inventory.Api.Authentication;
using Inventory.Api.ErrorHandling;
using Inventory.Application.Abstractions;
using Inventory.Application.Reservations;
using Inventory.Application.Stock;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

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
        Description = "Paste ONLY the access token from Identity's POST /api/auth/login."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() }
    });
});

// ---------------------------------------------------------------------
// 3. Database
// ---------------------------------------------------------------------
builder.Services.AddDbContext<InventoryDbContext>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("Database"));
});

builder.Services.AddScoped<IInventoryDbContext>(serviceProvider =>
    serviceProvider.GetRequiredService<InventoryDbContext>());

// ---------------------------------------------------------------------
// 4. Application services
// ---------------------------------------------------------------------
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IReservationService, ReservationService>();

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
