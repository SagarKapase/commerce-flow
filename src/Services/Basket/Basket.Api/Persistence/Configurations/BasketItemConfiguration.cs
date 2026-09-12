using Basket.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basket.Api.Persistence.Configurations;

internal sealed class BasketItemConfiguration : IEntityTypeConfiguration<BasketItem>
{
    public void Configure(EntityTypeBuilder<BasketItem> builder)
    {
        builder.ToTable("BasketItems");

        // ------------------------------------------------------------------
        // COMPOSITE PRIMARY KEY: (UserId, ProductId).
        //
        // This is the model expressing a business rule directly. "The same
        // product must never appear twice in one basket" is not a check we
        // remember to write - it is structurally impossible, because the two
        // rows would have the same key.
        //
        // It also makes the routes natural: DELETE /api/basket/items/{productId}
        // addresses exactly one row once you know the caller, and the caller
        // comes from the token.
        //
        // There is no surrogate Id column because nothing outside this service
        // ever refers to a basket line.
        // ------------------------------------------------------------------
        builder.HasKey(item => new { item.UserId, item.ProductId });

        builder.Property(item => item.ProductName)
            .IsRequired()
            .HasMaxLength(BasketItem.ProductNameMaxLength);

        // Same conversion as Catalog, for the same reason: SQLite has no
        // decimal, and EF would store one as TEXT where comparisons and
        // ordering are wrong. Money is an INTEGER count of minor units.
        //
        // Note this is DUPLICATED from Catalog's ProductConfiguration rather
        // than shared. Basket and Catalog agree that money has two decimal
        // places; they do not share a compiled type to say so.
        builder.Property(item => item.UnitPrice)
            .HasConversion(
                unitPrice => (long)(unitPrice * 100m),
                minorUnits => minorUnits / 100m)
            .HasColumnName("UnitPriceInMinorUnits")
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.Property(item => item.Quantity)
            .IsRequired();

        builder.Property(item => item.AddedAtUtc)
            .IsRequired();

        builder.Ignore(item => item.LineTotal);

        // Cascade: deleting a basket deletes its lines. Unlike Catalog's
        // products, a basket line has no meaning on its own and nothing else
        // points at it, so there is nothing to protect by restricting.
        builder.HasOne<CustomerBasket>()
            .WithMany(basket => basket.Items)
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deliberately absent: any foreign key on ProductId. Products live in
        // catalog.db, a different file this service cannot see. The reference
        // is by id only, and integrity is a runtime concern rather than a
        // database guarantee - which is the whole cost of database-per-service.
    }
}
