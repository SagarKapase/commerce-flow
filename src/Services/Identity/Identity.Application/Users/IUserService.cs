using Identity.Application.Users.Dtos;

namespace Identity.Application.Users;

public interface IUserService
{
    /// <returns>The user, or <c>null</c> when no user has that id.</returns>
    Task<UserResponse?> GetByIdAsync(Guid userId, CancellationToken cancellationToken);
}
