using System.Text;
using Identity.Api.ErrorHandling;
using Identity.Api.Seeding;
using Identity.Application.Abstractions;
using Identity.Application.Authentication;
using Identity.Application.Users;
using Identity.Infrastructure.Authentication;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
// Microsoft.OpenApi 2.x (which Swashbuckle 10 uses) moved these types out of
// the old Microsoft.OpenApi.Models namespace. Most tutorials online still show
// the old one - a good reminder to read the compiler error, not the blog post.
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------
// 1. MVC controllers
// ---------------------------------------------------------------------
builder.Services.AddControllers();

// ---------------------------------------------------------------------
// 2. Swagger, with a Bearer token box
//
// Without AddSecurityDefinition there is no "Authorize" button, and you
// cannot test [Authorize] endpoints from the UI at all.
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
        Description = "Paste ONLY the access token value. Swagger adds the \"Bearer \" prefix."
    });

    // Tells Swagger to send the token on every request once you have
    // authorised. The empty string array is the list of required OAuth
    // scopes - we have none.
    // Swashbuckle 10 hands you the document being built so the requirement can
    // point at the "Bearer" definition registered above by reference, rather
    // than repeating the whole scheme.
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() }
    });
});

// ---------------------------------------------------------------------
// 3. Database
// ---------------------------------------------------------------------
builder.Services.AddDbContext<IdentityServiceDbContext>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("Database"));
});

builder.Services.AddScoped<IIdentityDbContext>(serviceProvider =>
    serviceProvider.GetRequiredService<IdentityServiceDbContext>());

// ---------------------------------------------------------------------
// 4. Strongly typed configuration
//
// ValidateDataAnnotations + ValidateOnStart means a missing issuer or a
// signing key shorter than 32 characters fails the application AT STARTUP
// with a readable message - instead of throwing "IDX10653: key size is
// smaller than required" on the first login in production.
// ---------------------------------------------------------------------
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<RefreshTokenOptions>()
    .Bind(builder.Configuration.GetSection(RefreshTokenOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ---------------------------------------------------------------------
// 5. ASP.NET Core Identity
//
// AddIdentityCore, NOT AddIdentity. AddIdentity also registers COOKIE
// authentication and sets the default authentication scheme to cookies,
// which fights with JWT bearer: [Authorize] would try to redirect the
// caller to a non-existent /Account/Login page and return 302 instead of
// 401. Core gives us the user store, password hashing, validators and
// UserManager - everything an API needs and nothing it does not.
// ---------------------------------------------------------------------
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    // Spelled out rather than left to defaults, so the policy is visible.
    options.User.RequireUniqueEmail = true;

    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;

    // Symbols are not required: length and variety already do the work, and
    // forcing symbols pushes people towards "Password1!" patterns.
    options.Password.RequireNonAlphanumeric = false;

    // Brute-force defence. Five wrong guesses locks the account for five
    // minutes; AuthService drives this explicitly via AccessFailedAsync.
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.AllowedForNewUsers = true;
})
.AddRoles<ApplicationRole>()
.AddEntityFrameworkStores<IdentityServiceDbContext>();

// ---------------------------------------------------------------------
// 6. Authentication - VALIDATING tokens
//
// Read straight from configuration because AddJwtBearer needs the key while
// the container is still being built, before IOptions can be resolved.
// ---------------------------------------------------------------------
var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>()
    ?? throw new InvalidOperationException("The 'Jwt' configuration section is missing.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Legacy behaviour renames incoming claims: "sub" silently becomes
        // the long ClaimTypes.NameIdentifier URI, which is why so many
        // tutorials cannot find the claim they just put in the token.
        // Turning it off means what we put in is what we read out.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            // Is it from us?
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,

            // Is it meant for us?
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,

            // Was it signed with our key - i.e. not forged or tampered with?
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),

            // Has it expired?
            ValidateLifetime = true,

            // The default is FIVE MINUTES of tolerance for clock differences
            // between servers, which means an "expired" token keeps working
            // for five more minutes and makes expiry impossible to demo.
            // Everything here runs on one machine, so 30 seconds is plenty.
            ClockSkew = TimeSpan.FromSeconds(30),

            // Which claims populate User.Identity.Name and User.IsInRole.
            // These must match what JwtAccessTokenGenerator writes.
            NameClaimType = JwtRegisteredClaimNames.Name,
            RoleClaimType = JwtClaimNames.Role
        };
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------
// 7. Application services
// ---------------------------------------------------------------------
builder.Services.AddScoped<IAccessTokenGenerator, JwtAccessTokenGenerator>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();

// ---------------------------------------------------------------------
// 8. Error handling
// ---------------------------------------------------------------------
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Creates the admin account if it does not exist. Development only -
    // a production system gets its first admin through a deliberate,
    // audited process, not automatically at startup.
    await DevelopmentDataSeeder.SeedAdminUserAsync(app.Services, app.Configuration);
}

app.UseHttpsRedirection();

// ORDER MATTERS AND IS NOT NEGOTIABLE.
// UseAuthentication reads the Authorization header, validates the token and
// builds HttpContext.User. UseAuthorization then checks [Authorize] against
// that user. Swap them and User is still empty when the check runs, so every
// authenticated request returns 401 and it looks like the token is broken.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
