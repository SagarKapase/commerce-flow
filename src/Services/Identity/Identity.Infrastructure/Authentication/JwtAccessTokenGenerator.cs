using System.Security.Claims;
using System.Text;
using Identity.Application.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.Authentication;

/// <summary>
/// Creates signed JWTs.
///
/// WHAT A JWT ACTUALLY IS - three Base64Url segments joined by dots:
///   header.payload.signature
/// The header names the algorithm, the payload holds the claims, and the
/// signature is HMAC-SHA256 over "header.payload" using the signing key.
///
/// The payload is ENCODED, NOT ENCRYPTED. Anyone holding the token can read
/// every claim in it - paste one into jwt.io and see. Never put a secret in a
/// token. The signature does not hide anything; it only proves that nobody
/// altered it, because changing one byte of the payload invalidates it and an
/// attacker cannot produce a new signature without the key.
///
/// That is also why other services never call Identity to validate a token:
/// they hold the key, so they can verify the signature themselves. No network
/// call, no shared session store, and Catalog keeps working even when Identity
/// is down.
/// </summary>
public sealed class JwtAccessTokenGenerator : IAccessTokenGenerator
{
    private static readonly JsonWebTokenHandler TokenHandler = new();

    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;

    public JwtAccessTokenGenerator(IOptions<JwtOptions> options)
    {
        _options = options.Value;

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));

        _signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
    }

    public AccessToken Generate(
        Guid userId,
        string email,
        string fullName,
        IReadOnlyList<string> roles)
    {
        // Truncated to whole seconds because that is the resolution of the
        // "exp" claim. Without this, the expiry we report to the client can be
        // up to a second later than the one inside the token.
        var expiresAtUtc = DateTime.UtcNow
            .AddMinutes(_options.AccessTokenMinutes)
            .AddTicks(-(DateTime.UtcNow.Ticks % TimeSpan.TicksPerSecond));

        var claims = new List<Claim>
        {
            // "sub" (subject) is THE user identifier. Every other service will
            // read this claim to know who is calling - Basket will use it to
            // decide whose basket to load.
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Name, fullName),

            // "jti" is a unique id for this token. We do not use it yet; it is
            // what a token blacklist or replay check would key on later.
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString())
        };

        // One claim per role. A user in two roles gets two "role" claims -
        // repeated claim names are normal in JWTs.
        claims.AddRange(roles.Select(role => new Claim(JwtClaimNames.Role, role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Expires = expiresAtUtc,
            SigningCredentials = _signingCredentials
        };

        // The handler adds iat, nbf and exp itself and writes the three
        // segments.
        return new AccessToken(TokenHandler.CreateToken(descriptor), expiresAtUtc);
    }
}
