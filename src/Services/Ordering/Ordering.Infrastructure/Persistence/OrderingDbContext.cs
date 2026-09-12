using Microsoft.EntityFrameworkCore;
using Ordering.Domain.Entities;
using Ordering.Infrastructure.Persistence.Configurations;

namespace Ordering.Infrastructure.Persistence;

/// <summary>
/// The Ordering service's own database (ordering.db).
///
/// Notice this type is NOT exposed through an interface to the application
/// layer, unlike Catalog's ICatalogDbContext. Ordering.Application never sees a
/// DbContext or a DbSet at all - it talks to IOrderRepository, and this class
/// exists only for the repository and the EF tooling to use.
/// </summary>
public sealed class OrderingDbContext : DbContext
{
    public OrderingDbContext(DbContextOptions<OrderingDbContext> options)
        : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderItemConfiguration());
    }
}
