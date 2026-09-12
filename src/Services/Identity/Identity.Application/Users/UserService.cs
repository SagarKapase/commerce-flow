using Identity.Application.Users.Dtos;
using Microsoft.AspNetCore.Identity;

namespace Identity.Application.Users;

public sealed class UserService : IUserService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UserService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<UserResponse?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        // FindByIdAsync takes the key as a string even though ours is a Guid -
        // a consequence of UserManager being generic over the key type.
        var user = await _userManager.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return null;
        }

        var roles = (await _userManager.GetRolesAsync(user)).ToList();

        return UserMappings.ToResponse(user, roles);
    }
}
