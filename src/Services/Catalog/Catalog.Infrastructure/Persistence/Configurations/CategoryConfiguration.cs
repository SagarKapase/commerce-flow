using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// How a Category becomes a row.
///
/// This lives here, not on the entity as attributes, because table names,
/// column types and indexes are persistence decisions. Keeping them out of
/// Catalog.Domain is what lets that project reference nothing.
/// </summary>
internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");

        builder.HasKey(category => category.Id);

        // The domain generates the id (Guid.CreateVersion7) in Category.Create,
        // so the database must not try to supply one.
        builder.Property(category => category.Id)
            .ValueGeneratedNever();

        builder.Property(category => category.Name)
            .IsRequired()
            .HasMaxLength(Category.NameMaxLength);

        builder.Property(category => category.Slug)
            .IsRequired()
            .HasMaxLength(Category.SlugMaxLength);

        builder.Property(category => category.IsActive)
            .IsRequired();

        builder.Property(category => category.CreatedAtUtc)
            .IsRequired();

        // Slugs appear in URLs, so two categories may never share one. The
        // application checks first to give a friendly 409, but THIS is the
        // guarantee: it holds even when two requests race.
        builder.HasIndex(category => category.Slug)
            .IsUnique()
            .HasDatabaseName("IX_Categories_Slug");
    }
}
