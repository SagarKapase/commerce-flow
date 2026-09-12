using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("InventoryItems");

        // One stock record per product, enforced by the key itself.
        builder.HasKey(item => item.ProductId);

        builder.Property(item => item.ProductId)
            .ValueGeneratedNever();

        builder.Property(item => item.AvailableQuantity)
            .IsRequired();

        builder.Property(item => item.ReservedQuantity)
            .IsRequired();

        // ------------------------------------------------------------------
        // THE LINE THIS ENTIRE PHASE IS ABOUT.
        //
        // IsConcurrencyToken() changes the SQL EF generates for an UPDATE. It
        // adds the property's ORIGINAL value to the WHERE clause while writing
        // the NEW value in SET:
        //
        //     UPDATE "InventoryItems"
        //     SET "AvailableQuantity" = @p0, "ReservedQuantity" = @p1, "Version" = @p2
        //     WHERE "ProductId" = @p3 AND "Version" = @p4;
        //     SELECT changes();
        //
        // EF then checks how many rows changed. Expecting one and getting ZERO
        // means somebody else updated the row after we read it, and it throws
        // DbUpdateConcurrencyException.
        //
        // ON SQL SERVER you would use a rowversion column and the database
        // would maintain it for you. SQLite has no such type, so the domain
        // increments an int itself in InventoryItem.Touch(). That is not a
        // downgrade - it is provider-agnostic, and the mechanism is visible in
        // code you wrote rather than magic the database performs.
        //
        // This is OPTIMISTIC concurrency: assume conflicts are rare, detect
        // them, and make the loser retry. The pessimistic alternative - locking
        // the row on read - serialises every checkout in the shop behind
        // whoever is currently deciding, and turns a rare conflict into a
        // permanent queue.
        // ------------------------------------------------------------------
        builder.Property(item => item.Version)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(item => item.CreatedAtUtc)
            .IsRequired();
    }
}
