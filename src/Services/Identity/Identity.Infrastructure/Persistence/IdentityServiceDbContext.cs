using Identity.Application.Abstractions;
using Identity.Application.Users;
using Identity.Infrastructure.Persistence.Configurations;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence;

/// <summary>
/// The Identity service's own database (identity.db).
///
/// It derives from IdentityDbContext&lt;TUser, TRole, TKey&gt;, which brings
/// seven tables with it: AspNetUsers, AspNetRoles, AspNetUserRoles,
/// AspNetUserClaims, AspNetRoleClaims, AspNetUserLogins and AspNetUserTokens.
/// We use the first three; the rest exist because the framework's stores expect
/// them (external logins, per-user claims, 2FA tokens) and dropping them would
/// mean writing our own stores.
///
/// It is named IdentityServiceDbContext, not IdentityDbContext, because the
/// base class already owns that name and shadowing it makes every error message
/// ambiguous.
/// </summary>
public sealed class IdentityServiceDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>, IIdentityDbContext
{
    public IdentityServiceDbContext(DbContextOptions<IdentityServiceDbContext> options)
        : base(options)
    {
    }

    /// <summary>The one table that is ours rather than the framework's.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // MUST be called first. This is what actually defines the Identity
        // tables, their keys and their indexes. Forgetting it produces a
        // migration that creates no user tables at all - a classic and very
        // confusing failure.
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new ApplicationUserConfiguration());
        modelBuilder.ApplyConfiguration(new ApplicationRoleConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
    }
}
