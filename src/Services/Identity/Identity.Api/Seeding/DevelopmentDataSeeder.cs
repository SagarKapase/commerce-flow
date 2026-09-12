using Identity.Application.Users;
using Microsoft.AspNetCore.Identity;

namespace Identity.Api.Seeding;

/// <summary>
/// Creates the first administrator so there is somebody who can use the
/// Admin-only endpoints.
///
/// WHY NOT IN THE MIGRATION (like the roles are)?
/// Creating a user means hashing a password, which needs UserManager - a
/// service from the DI container, which a migration cannot reach. And a
/// password hash committed to source control would be the same on every
/// developer's machine and in every environment that ran the migration.
///
/// WHY DEVELOPMENT ONLY?
/// A production system creates its first admin through a deliberate, audited
/// step. Software that quietly grants itself an administrator at startup is a
/// backdoor, however well intentioned.
/// </summary>
internal static class DevelopmentDataSeeder
{
    public static async Task SeedAdminUserAsync(
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        // UserManager is registered as Scoped, and at startup we are outside
        // any request, so there is no scope. Creating one explicitly is the
        // correct way to use scoped services from application startup -
        // resolving a scoped service from the root provider would keep it
        // alive for the lifetime of the process.
        using var scope = serviceProvider.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(DevelopmentDataSeeder));

        var email = configuration["Seed:AdminEmail"];
        var password = configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("No admin seed configuration found; skipping.");
            return;
        }

        // Idempotent: safe to run on every startup.
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var admin = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            FullName = "CommerceFlow Administrator",
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(admin, password);

        if (!result.Succeeded)
        {
            logger.LogError(
                "Failed to seed the admin user: {Errors}",
                string.Join("; ", result.Errors.Select(error => error.Description)));

            return;
        }

        await userManager.AddToRoleAsync(admin, ApplicationRoles.Admin);

        logger.LogInformation("Seeded admin user {Email} with id {UserId}", email, admin.Id);
    }
}
