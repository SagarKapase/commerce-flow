using System.ComponentModel.DataAnnotations;

namespace Identity.Infrastructure.Authentication;

/// <summary>
/// JWT settings, bound from the "Jwt" section of configuration.
///
/// The DataAnnotations here are not decoration: Program.cs calls
/// ValidateDataAnnotations().ValidateOnStart(), so a missing issuer or a
/// too-short signing key stops the application at startup with a clear message
/// instead of producing a confusing IDX10653 exception on the first login
/// attempt in production.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Who issued the token. Validated by every consuming service.</summary>
    [Required]
    public string Issuer { get; init; } = string.Empty;

    /// <summary>Who the token is for. Validated by every consuming service.</summary>
    [Required]
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// The symmetric key used to sign and verify tokens. HMAC-SHA256 requires
    /// at least 256 bits, which is 32 ASCII characters.
    ///
    /// SYMMETRIC MEANS EVERY SERVICE THAT VALIDATES TOKENS ALSO HOLDS THE KEY
    /// THAT CREATES THEM. That is acceptable here because all services are ours
    /// and run on one machine. In production you would move to an asymmetric
    /// key pair (RS256): Identity keeps the private key and signs, everyone
    /// else fetches the public key and can only verify. Then a compromised
    /// Catalog cannot mint admin tokens.
    /// </summary>
    [Required]
    [MinLength(32, ErrorMessage = "Jwt:SigningKey must be at least 32 characters for HMAC-SHA256.")]
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>
    /// Short on purpose. A JWT cannot be revoked, so its lifetime is exactly
    /// how long a stolen one stays useful.
    /// </summary>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; init; } = 15;
}
