using Microsoft.AspNetCore.Identity;

namespace Identity.Application.Users;

/// <summary>
/// A role, e.g. "Admin" or "Customer".
///
/// It adds nothing to IdentityRole&lt;Guid&gt; today. It exists because the
/// DbContext and AddRoles&lt;T&gt;() need a concrete role type that matches our
/// Guid key, and because a named type is the place any future role metadata
/// (a description, a display name) would go without a migration of every
/// generic signature in the service.
/// </summary>
public sealed class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole()
    {
    }

    public ApplicationRole(string roleName)
        : base(roleName)
    {
    }
}
