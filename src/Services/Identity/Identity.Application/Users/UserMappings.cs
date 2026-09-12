using Identity.Application.Users.Dtos;

namespace Identity.Application.Users;

internal static class UserMappings
{
    /// <summary>
    /// Roles are passed in rather than read from the user, because
    /// ApplicationUser has no Roles property - membership lives in the
    /// AspNetUserRoles join table and is fetched through UserManager.
    /// </summary>
    public static UserResponse ToResponse(ApplicationUser user, IReadOnlyList<string> roles)
    {
        return new UserResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.FullName,
            roles,
            user.CreatedAtUtc);
    }
}
