namespace Identity.Application.Users.Dtos;

/// <summary>
/// A user, as the API describes one.
///
/// Contrast with ApplicationUser, which also carries PasswordHash,
/// SecurityStamp, ConcurrencyStamp, AccessFailedCount and LockoutEnd. Returning
/// the entity would leak every one of those. This is the single clearest
/// example in the project of why DTOs are not busywork.
/// </summary>
public sealed record UserResponse(
    Guid Id,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles,
    DateTime CreatedAtUtc);
