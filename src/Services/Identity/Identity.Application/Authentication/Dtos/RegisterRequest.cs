using System.ComponentModel.DataAnnotations;
using Identity.Application.Users;

namespace Identity.Application.Authentication.Dtos;

/// <summary>The body of POST /api/auth/register.</summary>
public sealed class RegisterRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Only the length is checked here. The REAL policy (digits, upper case,
    /// lower case, reuse of known-bad passwords) is enforced by ASP.NET Core
    /// Identity's password validators inside UserManager.CreateAsync, and its
    /// failures come back as a 400 listing exactly which rules were broken.
    ///
    /// Two layers on purpose: this one gives instant feedback on an obviously
    /// too-short value without a database round trip; that one is the authority.
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; init; } = string.Empty;

    [Required]
    [StringLength(ApplicationUser.FullNameMaxLength, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;
}
