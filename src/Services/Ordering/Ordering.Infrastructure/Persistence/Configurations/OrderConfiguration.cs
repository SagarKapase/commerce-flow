using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering.Domain.Entities;
using Ordering.Domain.ValueObjects;

namespace Ordering.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.HasKey(order => order.Id);

        builder.Property(order => order.Id)
            .ValueGeneratedNever();

        builder.Property(order => order.CustomerId)
            .IsRequired();

        // Stored as text, so the table reads "Confirmed" rather than 4. Same
        // reasoning as Inventory's ReservationStatus: the person reading this
        // table at 2am is debugging and does not have the enum open.
        builder.Property(order => order.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(order => order.FailureReason)
            .HasMaxLength(500);

        builder.Property(order => order.CreatedAtUtc)
            .IsRequired();

        // ------------------------------------------------------------------
        // OWNED VALUE OBJECT.
        //
        // Address is a record with no identity of its own, so it gets no table
        // and no key. OwnsOne folds it into the Orders row as four columns.
        // The database sees ShippingLine1, ShippingCity, ShippingPostalCode,
        // ShippingCountry; the domain sees one Address object with value
        // equality.
        //
        // Why not a separate Addresses table with a foreign key? Because that
        // would give the address an identity it does not have, and invite the
        // question "is this the same address row as that order's?" - which is
        // meaningless. An address is a value, like a number. Two orders to the
        // same street are two independent copies, and a customer moving house
        // must not silently rewrite where last year's parcels went.
        //
        // The column prefix is set explicitly rather than left to EF's default
        // ("ShippingAddress_Line1"), because these end up in reports and
        // queries people read.
        // ------------------------------------------------------------------
        builder.OwnsOne(order => order.ShippingAddress, address =>
        {
            address.Property(value => value.Line1)
                .HasColumnName("ShippingLine1")
                .HasMaxLength(Address.Line1MaxLength)
                .IsRequired();

            address.Property(value => value.City)
                .HasColumnName("ShippingCity")
                .HasMaxLength(Address.CityMaxLength)
                .IsRequired();

            address.Property(value => value.PostalCode)
                .HasColumnName("ShippingPostalCode")
                .HasMaxLength(Address.PostalCodeMaxLength)
                .IsRequired();

            address.Property(value => value.Country)
                .HasColumnName("ShippingCountry")
                .HasMaxLength(Address.CountryMaxLength)
                .IsRequired();
        });

        // An owned type is loaded with its owner automatically - no Include
        // needed, because it is the same row.
        builder.Navigation(order => order.ShippingAddress).IsRequired();

        // TotalAmount is computed from the lines. Mapping it would create a
        // column nobody updates, which would disagree with the items the first
        // time anything changed. Derived data belongs in code.
        builder.Ignore(order => order.TotalAmount);
        builder.Ignore(order => order.CanBeCancelled);

        // The items collection is private behind a read-only property, so EF
        // must be pointed at the backing field.
        builder.Metadata
            .FindNavigation(nameof(Order.Items))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(order => order.Items)
            .HasField("_items")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // "My orders, newest first" is the most common query in this service,
        // and it filters on CustomerId and sorts on CreatedAtUtc. A composite
        // index in that shape lets SQLite satisfy both from the index alone
        // instead of sorting the matching rows afterwards.
        builder.HasIndex(order => new { order.CustomerId, order.CreatedAtUtc })
            .HasDatabaseName("IX_Orders_CustomerId_CreatedAtUtc");

        // Supports the administrator's "show me everything Pending" filter.
        builder.HasIndex(order => order.Status)
            .HasDatabaseName("IX_Orders_Status");
    }
}
