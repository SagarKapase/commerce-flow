using Identity.Application.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Abstractions;

/// <summary>
/// The application layer's view of the database - and note how small it is.
///
/// It exposes ONLY RefreshTokens. Users and roles are reached through
/// UserManager&lt;ApplicationUser&gt; and RoleManager, which are ASP.NET Core
/// Identity's own repository over this very same DbContext: they know how to
/// normalise emails, hash passwords, stamp security stamps and maintain the
/// join tables, and doing that by hand through a DbSet would be a bug factory.
///
/// So there are two persistence paths in this service, on purpose:
///   - UserManager        for anything Identity owns
///   - IIdentityDbContext for the one table Identity does not know about
/// </summary>
public interface IIdentityDbContext
{
    DbSet<RefreshToken> RefreshTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
