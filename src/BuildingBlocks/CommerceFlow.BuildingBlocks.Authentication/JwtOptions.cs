using System.ComponentModel.DataAnnotations;

namespace CommerceFlow.BuildingBlocks.Authentication;

/// <summary>
/// What a service needs in order to VALIDATE a CommerceFlow token.
///
/// Three properties, and note again what is missing compared with the copy the
/// Identity service keeps: there is no AccessTokenMinutes here, because only
/// the issuer decides how long a token lives. A consumer just reads the "exp"
/// claim that is already inside it.
///
/// Identity keeps its own JwtOptions and does NOT reference this library. That
/// is deliberate: Identity is the issuer, its needs are different, and coupling
/// the issuer to the consumers' shared library would mean a change for
/// consumers forces a redeploy of the thing that mints tokens.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Must equal the "iss" claim. Stops a token from another system being accepted.</summary>
    [Required]
    public string Issuer { get; init; } = string.Empty;

    /// <summary>Must equal the "aud" claim. Stops a token issued for another application being replayed here.</summary>
    [Required]
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// The shared symmetric key. Because HMAC-SHA256 is symmetric, the key that
    /// VERIFIES a signature is the key that CREATES one - so every service
    /// holding this could mint an admin token for itself.
    ///
    /// Production moves to asymmetric signing (RS256): Identity keeps a private
    /// key and signs; everyone else gets only the public key and can verify but
    /// never forge. That change would happen in this one file plus the
    /// extension method next to it, which is a decent argument for having
    /// extracted them.
    /// </summary>
    [Required]
    [MinLength(32, ErrorMessage = "Jwt:SigningKey must be at least 32 characters for HMAC-SHA256.")]
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>
    /// Tolerance for clock drift between machines when checking "exp".
    ///
    /// The library default is FIVE MINUTES, which quietly turns a 15-minute
    /// access token into a 20-minute one. Thirty seconds is a decision; five
    /// minutes is an inheritance.
    /// </summary>
    [Range(0, 300)]
    public int ClockSkewSeconds { get; init; } = 30;
}
