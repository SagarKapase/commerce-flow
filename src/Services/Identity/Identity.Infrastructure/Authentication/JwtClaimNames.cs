namespace Identity.Infrastructure.Authentication;

/// <summary>
/// Claim names this system chooses for itself.
///
/// The registered names (sub, email, jti, exp) are standard and come from
/// JwtRegisteredClaimNames. "role" is not standard, so we pin it here and
/// configure TokenValidationParameters.RoleClaimType to match.
///
/// WHY NOT ClaimTypes.Role:
/// ClaimTypes.Role is the URI "http://schemas.microsoft.com/ws/2008/06/identity/claims/role".
/// Putting a 70-character URI in every token, for every role, wastes space in
/// every request header for no benefit. Short names are the norm in modern JWTs.
/// </summary>
public static class JwtClaimNames
{
    public const string Role = "role";
}
