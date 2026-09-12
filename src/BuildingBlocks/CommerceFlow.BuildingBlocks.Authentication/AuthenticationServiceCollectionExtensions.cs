using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace CommerceFlow.BuildingBlocks.Authentication;

/// <summary>
/// The forty lines that used to sit in four Program.cs files.
///
/// A NOTE ON HIDING THINGS. The rule for this project has been "no magic - show
/// the explicit configuration first". That rule has been kept: you wrote this
/// block by hand in Catalog, then Basket, then Inventory, then Ordering. Only
/// now, when you can recite it, does it disappear behind one method call.
///
/// That order matters. An extension method you understand is a convenience; an
/// extension method you inherited is a black box you will be afraid to change
/// the first time authentication misbehaves. The comments below are kept in
/// full for exactly that moment.
/// </summary>
public static class AuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Adds JWT bearer authentication configured for CommerceFlow tokens, plus
    /// the authorization services that <c>[Authorize]</c> needs.
    ///
    /// Call it in Program.cs:
    /// <code>builder.Services.AddCommerceFlowJwtAuthentication(builder.Configuration);</code>
    ///
    /// Then, in the pipeline and IN THIS ORDER:
    /// <code>
    /// app.UseAuthentication();   // reads the header, builds HttpContext.User
    /// app.UseAuthorization();    // checks [Authorize] against that User
    /// </code>
    /// Reversed, the authorization check runs against an empty principal, every
    /// authenticated request returns 401, and it looks like the tokens are
    /// broken when the pipeline is. That ordering cannot be moved into this
    /// method, because middleware order is the application's decision.
    /// </summary>
    public static IServiceCollection AddCommerceFlowJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bound and validated at STARTUP. A missing issuer or a signing key
        // under 32 characters stops the service immediately with a readable
        // message, instead of rejecting every token at runtime and looking
        // like an authentication bug.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Read directly as well, because AddJwtBearer needs the key while the
        // container is still being built and IOptions cannot be resolved yet.
        var jwtOptions = configuration
            .GetSection(JwtOptions.SectionName)
            .Get<JwtOptions>()
            ?? throw new InvalidOperationException(
                $"The '{JwtOptions.SectionName}' configuration section is missing.");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Legacy behaviour renames incoming claims: "sub" silently
                // becomes the long ClaimTypes.NameIdentifier URI, which is why
                // so many tutorials cannot find the claim they just put in the
                // token. Off means what Identity put in is what we read out.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Is it from our Identity service?
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,

                    // Was it meant for this system, or replayed from another?
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,

                    // Was it signed with the key we trust - not forged, not edited?
                    // THIS is the check that means no service ever has to call
                    // Identity. Validation is local arithmetic over the token
                    // and a key, so every service keeps enforcing authorization
                    // even when Identity is down.
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),

                    // Is it still inside its lifetime?
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(jwtOptions.ClockSkewSeconds),

                    // Which claim [Authorize(Roles = ...)] and User.IsInRole
                    // consult. Get this wrong and a perfectly valid admin token
                    // produces a 403 with nothing in the logs to explain it.
                    RoleClaimType = JwtClaimNames.Role
                };
            });

        // Registers the authorization services [Authorize] depends on. Bundled
        // here because all four services call it identically right after
        // authentication; the day one of them needs a custom policy, it can add
        // that on top - AddAuthorization is additive.
        services.AddAuthorization();

        return services;
    }
}
