using Catalog.Application.Abstractions;
using Catalog.Domain.Entities;
using Catalog.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure.Persistence;

/// <summary>
/// The Catalog service's own database. Nothing outside this service opens
/// catalog.db - that is what "database per service" means in practice.
///
/// It implements ICatalogDbContext so the application layer can use it without
/// referencing this project.
/// </summary>
public sealed class CatalogDbContext : DbContext, ICatalogDbContext
{
    // The options (which provider, which connection string) are supplied by
    // dependency injection in Program.cs. This class does not know it is
    // talking to SQLite - swapping to PostgreSQL never touches this file.
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options)
        : base(options)
    {
    }

    // Expression-bodied so there is no settable auto-property for the nullable
    // analyser to complain about. Set<T>() returns the same DbSet instance EF
    // Core would have injected.
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Listed one by one on purpose. The one-liner
        // `modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly)`
        // scans the assembly with reflection and would work, but then a new
        // configuration file silently joins the model and a mistyped one
        // silently does not. Here, the model is whatever this method says it is.
        modelBuilder.ApplyConfiguration(new CategoryConfiguration());
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
    }
}
