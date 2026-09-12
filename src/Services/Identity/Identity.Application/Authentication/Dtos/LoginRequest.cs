using System.ComponentModel.DataAnnotations;

namespace Identity.Application.Authentication.Dtos;

/// <summary>The body of POST /api/auth/login.</summary>
public sealed class LoginRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Deliberately NOT decorated with the password policy.
    ///
    /// Two reasons. First, a login endpoint that replies "your password must
    /// contain a digit" is telling an attacker the policy for free. Second,
    /// passwords stored before a policy change are still valid; rejecting them
    /// at the door would lock out real users who could otherwise sign in and
    /// update their password.
    /// </summary>
    [Required]
    public string Password { get; init; } = string.Empty;
}
