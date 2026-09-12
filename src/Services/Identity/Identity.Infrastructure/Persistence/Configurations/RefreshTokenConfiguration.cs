using Identity.Application.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.HasKey(token => token.Id);

        builder.Property(token => token.Id)
            .ValueGeneratedNever();

        builder.Property(token => token.TokenHash)
            .IsRequired()
            .HasMaxLength(RefreshToken.TokenHashLength);

        builder.Property(token => token.CreatedAtUtc).IsRequired();
        builder.Property(token => token.ExpiresAtUtc).IsRequired();

        // Every refresh request looks a token up by its hash, so this index is
        // the difference between an index seek and a full table scan on the
        // busiest query in the service. Unique, because two rows must never
        // hash to the same value.
        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("IX_RefreshTokens_TokenHash");

        // Used by reuse detection, which revokes every active token for a user.
        builder.HasIndex(token => token.UserId)
            .HasDatabaseName("IX_RefreshTokens_UserId");

        // HasOne<ApplicationUser>() with no lambda: the relationship exists in
        // the database, but RefreshToken has no navigation property pointing
        // back at the user. It only ever needs the id.
        //
        // Cascade here, unlike Catalog's Restrict: a deleted user's tokens are
        // meaningless and should go with them. Deletion behaviour is a business
        // decision, not a default to copy.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
