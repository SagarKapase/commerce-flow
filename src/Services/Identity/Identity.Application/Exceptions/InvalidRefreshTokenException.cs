namespace Identity.Application.Exceptions;

/// <summary>
/// Thrown when a refresh token is unknown, expired or already revoked.
/// Maps to 401 Unauthorized - the correct answer is "log in again".
///
/// One message covers all three cases for the same reason login has one
/// message: an attacker holding a stolen token should not be told whether it
/// was wrong, stale or already burned.
/// </summary>
public sealed class InvalidRefreshTokenException : IdentityApplicationException
{
    public InvalidRefreshTokenException()
        : base("The refresh token is invalid or has expired. Please sign in again.")
    {
    }
}
