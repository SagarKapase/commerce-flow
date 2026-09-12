using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

internal sealed class InventoryReservationConfiguration : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.ToTable("Reservations");

        builder.HasKey(reservation => reservation.Id);

        builder.Property(reservation => reservation.Id)
            .ValueGeneratedNever();

        builder.Property(reservation => reservation.ReferenceId)
            .IsRequired();

        // ------------------------------------------------------------------
        // The enum is stored as TEXT, not as its underlying integer.
        //
        // Open Reservations in a database browser and you read "Held",
        // "Confirmed", "Released" instead of 1, 2, 3 - which matters more than
        // it sounds, because the person reading that table at 2am is debugging
        // and does not have the enum definition open.
        //
        // It also survives refactoring: reorder the enum members and an int
        // column silently remaps every existing row to the wrong meaning. The
        // cost is a few bytes and a slightly wider index. Worth it.
        // ------------------------------------------------------------------
        builder.Property(reservation => reservation.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(reservation => reservation.CreatedAtUtc)
            .IsRequired();

        // Phase 13 will make reservations idempotent by looking up an existing
        // one for a reference id before creating a second. This index is what
        // makes that lookup cheap. It is NOT unique yet - deciding that one
        // reference may only ever have one reservation is a real business rule,
        // and it belongs in the phase that implements it.
        builder.HasIndex(reservation => reservation.ReferenceId)
            .HasDatabaseName("IX_Reservations_ReferenceId");

        // The lines collection is private behind a read-only property, so EF
        // must be pointed at the backing field - same as the basket's items.
        builder.Metadata
            .FindNavigation(nameof(InventoryReservation.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(reservation => reservation.Lines)
            .HasField("_lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
