using Identity.Application.Authentication.Dtos;
using Identity.Application.Users.Dtos;

namespace Identity.Application.Authentication;

public interface IAuthService
{
    /// <summary>Creates an account. Does NOT sign the user in.</summary>
    Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Exchanges a valid refresh token for a new access + refresh token pair.
    /// The token presented is always consumed - see AuthService for why.
    /// </summary>
    Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes a refresh token. Always succeeds, even for an unknown token, so
    /// that logging out cannot be used to probe which tokens exist.
    /// </summary>
    Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken);
}
