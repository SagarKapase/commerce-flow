using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering.Domain.Entities;

namespace Ordering.Infrastructure.Persistence.Configurations;

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");

        // Composite key: a product appears at most once per order.
        builder.HasKey(item => new { item.OrderId, item.ProductId });

        builder.Property(item => item.ProductName)
            .IsRequired()
            .HasMaxLength(OrderItem.ProductNameMaxLength);

        // The fourth copy of this converter (Catalog, Basket, Ordering, and the
        // same idea in Inventory's quantities). Still duplicated, still on
        // purpose: it is part of each service's own schema, not shared plumbing.
        // Sharing it would mean a change to how money is stored forces every
        // service to migrate at the same moment.
        builder.Property(item => item.UnitPrice)
            .HasConversion(
                unitPrice => (long)(unitPrice * 100m),
                minorUnits => minorUnits / 100m)
            .HasColumnName("UnitPriceInMinorUnits")
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.Property(item => item.Quantity)
            .IsRequired();

        builder.Ignore(item => item.LineTotal);

        builder.HasOne<Order>()
            .WithMany(order => order.Items)
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
