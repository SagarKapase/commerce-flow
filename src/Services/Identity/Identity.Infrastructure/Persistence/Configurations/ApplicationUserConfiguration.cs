using Identity.Application.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures only the columns WE added. Everything else on AspNetUsers is
/// already configured by IdentityDbContext.OnModelCreating.
/// </summary>
internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(user => user.FullName)
            .IsRequired()
            .HasMaxLength(ApplicationUser.FullNameMaxLength);

        builder.Property(user => user.CreatedAtUtc)
            .IsRequired();

        // The id is generated in AuthService (Guid.CreateVersion7), not by EF.
        builder.Property(user => user.Id)
            .ValueGeneratedNever();
    }
}
