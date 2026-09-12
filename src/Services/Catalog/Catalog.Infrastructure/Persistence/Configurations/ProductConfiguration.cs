using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");

        builder.HasKey(product => product.Id);

        builder.Property(product => product.Id)
            .ValueGeneratedNever();

        // HasMaxLength is not enforced by SQLite - it ignores declared lengths
        // and stores whatever you give it. It is still worth setting: it
        // documents the intent, it is what our validation agrees with, and on
        // SQL Server or PostgreSQL the very same model produces a real
        // nvarchar(200) / varchar(200) constraint.
        builder.Property(product => product.Name)
            .IsRequired()
            .HasMaxLength(Product.NameMaxLength);

        builder.Property(product => product.Description)
            .IsRequired()
            .HasMaxLength(Product.DescriptionMaxLength);

        builder.Property(product => product.Sku)
            .IsRequired()
            .HasMaxLength(Product.SkuMaxLength);

        // ------------------------------------------------------------------
        // MONEY ON SQLITE - the most important line in this file.
        //
        // SQLite has no decimal type. Left alone, EF Core stores a decimal as
        // TEXT and warns that comparisons are not supported - which means
        // `WHERE Price >= 1000` would compare strings, and "9.99" would sort
        // after "1000.00". Our price-range filter would return wrong rows and
        // never throw.
        //
        // So we convert: the domain keeps an exact decimal, the database keeps
        // an INTEGER count of minor units (paise/cents). Integers sort and
        // compare correctly, and 89.99 is stored as exactly 8999 - no
        // floating-point drift, which is why we did not simply convert to
        // double.
        //
        // The column is named PriceInMinorUnits so nobody reading the database
        // mistakes 8999 for eight thousand rupees.
        // ------------------------------------------------------------------
        builder.Property(product => product.Price)
            .HasConversion(
                price => (long)(price * 100m),
                minorUnits => minorUnits / 100m)
            .HasColumnName("PriceInMinorUnits")
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.Property(product => product.IsActive)
            .IsRequired();

        // DateTime is stored as TEXT in ISO-8601 form, e.g.
        // "2026-09-07 18:30:00.1234567". Readable in any SQLite browser, and
        // sortable as text because the format is fixed-width and ordered.
        builder.Property(product => product.CreatedAtUtc)
            .IsRequired();

        // Uniqueness of SKU is a business rule the database enforces for us.
        builder.HasIndex(product => product.Sku)
            .IsUnique()
            .HasDatabaseName("IX_Products_Sku");

        // Not unique - just an index. Filtering by category is the most common
        // query this table will ever see, and without this the database scans
        // every row. Foreign keys are NOT indexed automatically in SQLite.
        builder.HasIndex(product => product.CategoryId)
            .HasDatabaseName("IX_Products_CategoryId");

        // WithMany() with no argument: the relationship exists, but Category
        // has no Products collection on the other side. We only navigate one
        // way, so we only model one way.
        //
        // DeleteBehavior.Restrict: the database refuses to delete a category
        // that still has products, instead of silently cascading and deleting
        // the products with it.
        builder.HasOne(product => product.Category)
            .WithMany()
            .HasForeignKey(product => product.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
