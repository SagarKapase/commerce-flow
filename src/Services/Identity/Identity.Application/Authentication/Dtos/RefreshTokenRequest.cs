using System.ComponentModel.DataAnnotations;

namespace Identity.Application.Authentication.Dtos;

/// <summary>
/// The body of POST /api/auth/refresh and POST /api/auth/logout.
///
/// Note what it does NOT contain: a user id. The refresh token itself proves
/// who you are. Taking a user id from the caller and trusting it would let
/// anyone refresh anyone's session - the same mistake we will avoid in Basket
/// when we read the owner from the token instead of the URL.
/// </summary>
public sealed class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}
