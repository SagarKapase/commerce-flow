using System.ComponentModel.DataAnnotations;

namespace Identity.Application.Authentication;

/// <summary>
/// How long a refresh token lives. Bound from the "RefreshToken" section of
/// appsettings.json in Program.cs.
///
/// WHY THIS IS SEPARATE FROM JwtOptions:
/// JwtOptions (issuer, audience, signing key, algorithm) is an implementation
/// detail of one token format and lives in Identity.Infrastructure. How long a
/// session may last is a policy decision the use case owns, so it lives here.
/// Each layer configures what it is responsible for.
/// </summary>
public sealed class RefreshTokenOptions
{
    public const string SectionName = "RefreshToken";

    [Range(1, 365)]
    public int LifetimeDays { get; init; } = 7;

    public TimeSpan Lifetime => TimeSpan.FromDays(LifetimeDays);
}
