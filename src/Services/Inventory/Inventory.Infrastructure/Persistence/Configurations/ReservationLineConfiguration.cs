using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

internal sealed class ReservationLineConfiguration : IEntityTypeConfiguration<ReservationLine>
{
    public void Configure(EntityTypeBuilder<ReservationLine> builder)
    {
        builder.ToTable("ReservationLines");

        // Composite key again, same reasoning as the basket's items: a product
        // may appear at most once per reservation, and the key makes the
        // alternative unrepresentable rather than merely discouraged.
        builder.HasKey(line => new { line.ReservationId, line.ProductId });

        builder.Property(line => line.Quantity)
            .IsRequired();

        // Cascade: a line has no meaning without its reservation.
        //
        // Note there is deliberately NO foreign key from ReservationLine to
        // InventoryItems, even though both tables live in this one database and
        // one could exist. A reservation is a historical record of what was
        // held; if a stock record were ever removed, the reservation should
        // still describe what happened.
        builder.HasOne<InventoryReservation>()
            .WithMany(reservation => reservation.Lines)
            .HasForeignKey(line => line.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
