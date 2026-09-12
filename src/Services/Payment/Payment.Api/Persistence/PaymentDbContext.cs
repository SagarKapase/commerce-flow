using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Persistence.Configurations;

namespace Payment.Api.Persistence;

public sealed class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options)
        : base(options)
    {
    }

    public DbSet<OrderPayment> Payments => Set<OrderPayment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new OrderPaymentConfiguration());
    }
}
