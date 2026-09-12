namespace Identity.Application.Abstractions;

/// <summary>An access token and the moment it stops being valid.</summary>
public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);

/// <summary>
/// Turns a user into an access token.
///
/// WHY THIS ABSTRACTION IS JUSTIFIED (using the four questions):
/// 1. Problem: AuthService must produce a token, but creating a JWT needs
///    signing keys, a token handler and a crypto library - all infrastructure.
/// 2. What it solves: the application layer states its need ("a token for this
///    user with these roles") without naming JWT, HMAC or a signing key.
/// 3. Why now: this is the only seam in the service that would change if we
///    moved from symmetric HMAC to RSA, or to reference tokens - which is a
///    realistic production step, not a hypothetical.
/// 4. Simpler alternative rejected: build the JWT inline in AuthService. That
///    would drag Microsoft.IdentityModel into the Application project and put
///    key handling in the middle of business flow.
/// </summary>
public interface IAccessTokenGenerator
{
    AccessToken Generate(
        Guid userId,
        string email,
        string fullName,
        IReadOnlyList<string> roles);
}
