using Identity.Application.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// Seeds the two roles into the migration itself.
///
/// WHY HasData AND NOT A STARTUP SEEDER:
/// Roles are static reference data - "Admin" and "Customer" are part of the
/// schema's meaning, they never change per environment, and they must exist
/// before any user can be assigned one. HasData puts them in the migration, so
/// applying the migration produces a correct database. A startup seeder would
/// mean a freshly migrated database is briefly wrong.
///
/// The admin USER is seeded differently (see DevelopmentDataSeeder), because
/// creating one requires hashing a password, which needs UserManager - a
/// service, which a migration has no access to. Also, a password hash baked
/// into source control is a genuinely bad idea.
///
/// THE GOTCHA: every value below must be a hard-coded constant. Using
/// Guid.NewGuid() or DateTime.Now here would make the model change on every
/// build, and EF would generate an endless stream of "seed data changed"
/// migrations. That is why ConcurrencyStamp is a fixed string rather than the
/// random one Identity normally assigns.
/// </summary>
internal sealed class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public static readonly Guid AdminRoleId = new("11111111-1111-1111-1111-111111111111");

    public static readonly Guid CustomerRoleId = new("22222222-2222-2222-2222-222222222222");

    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        builder.Property(role => role.Id)
            .ValueGeneratedNever();

        builder.HasData(
            new ApplicationRole
            {
                Id = AdminRoleId,
                Name = ApplicationRoles.Admin,

                // NormalizedName is what Identity actually searches on. Getting
                // it wrong means IsInRole and AddToRoleAsync silently fail to
                // match.
                NormalizedName = ApplicationRoles.Admin.ToUpperInvariant(),
                ConcurrencyStamp = "b0a1f6c6-0000-4000-8000-000000000001"
            },
            new ApplicationRole
            {
                Id = CustomerRoleId,
                Name = ApplicationRoles.Customer,
                NormalizedName = ApplicationRoles.Customer.ToUpperInvariant(),
                ConcurrencyStamp = "b0a1f6c6-0000-4000-8000-000000000002"
            });
    }
}
