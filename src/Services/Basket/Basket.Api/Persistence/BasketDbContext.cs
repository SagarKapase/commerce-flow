using Basket.Api.Domain;
using Basket.Api.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Basket.Api.Persistence;

/// <summary>
/// The Basket service's own database (basket.db).
///
/// There is no ICatalogDbContext-style interface here, because there is no
/// separate Application project to keep at arm's length - BasketService sits in
/// the same assembly and uses this type directly. One less indirection that
/// would protect nothing.
/// </summary>
public sealed class BasketDbContext : DbContext
{
    public BasketDbContext(DbContextOptions<BasketDbContext> options)
        : base(options)
    {
    }

    public DbSet<CustomerBasket> Baskets => Set<CustomerBasket>();

    // Exposed so EF can be queried directly where useful, but note that
    // BasketService never adds or removes items through this set - every change
    // goes through the aggregate root, and EF works out the INSERT/DELETE from
    // the resulting collection.
    public DbSet<BasketItem> BasketItems => Set<BasketItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new CustomerBasketConfiguration());
        modelBuilder.ApplyConfiguration(new BasketItemConfiguration());
    }
}
