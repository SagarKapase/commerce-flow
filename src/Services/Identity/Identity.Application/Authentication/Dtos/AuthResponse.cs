namespace Identity.Application.Authentication.Dtos;

/// <summary>
/// What login and refresh return.
///
/// The client stores both tokens, sends the access token on every request as
/// "Authorization: Bearer &lt;token&gt;", and calls /api/auth/refresh with the
/// refresh token when the access token expires.
///
/// Roles are included as a convenience for UIs deciding what to render. They
/// are NOT the security boundary - the roles that matter are the ones inside
/// the signed token, which a client cannot alter without invalidating the
/// signature.
/// </summary>
public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    Guid UserId,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles);
