using CommerceFlow.BuildingBlocks.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Payment.Api.ErrorHandling;
using Payment.Api.Gateway;
using Payment.Api.Payments;
using Payment.Api.Persistence;

// =====================================================================
// COMPOSITION ROOT
//
// ONE PROJECT, like Basket - and for the same reason. A simulated payment
// has almost no domain: a record, three states, and a decision made by a
// switch statement. Domain / Application / Infrastructure assemblies here
// would be four files of pass-through code and three project references to
// justify them.
//
// The seam that DOES matter is IPaymentGateway, and it is an interface, not
// an assembly boundary. Put the seam where the world changes.
// =====================================================================

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

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
            "Test tokens for the simulated gateway: tok_success, tok_declined, " +
            "tok_insufficient_funds, tok_timeout."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() }
    });
});

builder.Services.AddDbContext<PaymentDbContext>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("Database"));
});

// ---------------------------------------------------------------------
// The payment provider.
//
// One line to swap the fake for the real thing. That is the whole return on
// having an interface here: a StripePaymentGateway would implement the same
// method and nothing above it would change - not PaymentService, not the
// controller, not the database schema.
// ---------------------------------------------------------------------
builder.Services.Configure<PaymentSimulationOptions>(
    builder.Configuration.GetSection(PaymentSimulationOptions.SectionName));

builder.Services.AddScoped<IPaymentGateway, SimulatedPaymentGateway>();
builder.Services.AddScoped<IPaymentService, PaymentService>();

builder.Services.AddCommerceFlowJwtAuthentication(builder.Configuration);

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
