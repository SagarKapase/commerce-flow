using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payment.Api.Domain;

namespace Payment.Api.Persistence.Configurations;

internal sealed class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("Payments");

        builder.HasKey(payment => payment.Id);

        builder.Property(payment => payment.Id)
            .ValueGeneratedNever();

        // ------------------------------------------------------------------
        // THE MOST IMPORTANT LINE IN THIS SERVICE.
        //
        // One payment per order, enforced by the database. Not "we check
        // first" - CHECKED FIRST AND ALSO IMPOSSIBLE.
        //
        // PaymentService does look for an existing payment before creating
        // one, and that check is worth having because it gives a clean answer
        // instead of a constraint violation. But between that SELECT and the
        // INSERT there is a window, and under a double-clicked button or a
        // client retry that window is exactly when the second request arrives.
        // Only the index closes it.
        //
        // Same relationship as Catalog's SKU check and its unique index - but
        // the stakes are different. A duplicate product is embarrassing; a
        // duplicate CHARGE is a chargeback, a support call, and a customer who
        // does not come back. If you take one line from this whole project into
        // a real payment system, take this one.
        // ------------------------------------------------------------------
        builder.HasIndex(payment => payment.OrderId)
            .IsUnique()
            .HasDatabaseName("IX_Payments_OrderId");

        builder.Property(payment => payment.CustomerId)
            .IsRequired();

        // Money as an INTEGER count of minor units - the same converter as
        // Catalog, Basket and Ordering, duplicated in each service's own
        // schema on purpose. SQLite stores decimals as TEXT, where comparisons
        // sort lexicographically and 9.99 comes after 1000.00.
        builder.Property(payment => payment.Amount)
            .HasConversion(
                amount => (long)(amount * 100m),
                minorUnits => minorUnits / 100m)
            .HasColumnName("AmountInMinorUnits")
            .HasColumnType("INTEGER")
            .IsRequired();

        // As text, so the table reads "Succeeded" rather than 2.
        builder.Property(payment => payment.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(payment => payment.ProviderReference)
            .HasMaxLength(OrderPayment.ProviderReferenceMaxLength);

        builder.Property(payment => payment.FailureReason)
            .HasMaxLength(OrderPayment.FailureReasonMaxLength);

        builder.Property(payment => payment.CreatedAtUtc)
            .IsRequired();

        // Reconciliation queries - "show me everything that failed yesterday" -
        // are the bread and butter of a finance team.
        builder.HasIndex(payment => payment.Status)
            .HasDatabaseName("IX_Payments_Status");
    }
}
