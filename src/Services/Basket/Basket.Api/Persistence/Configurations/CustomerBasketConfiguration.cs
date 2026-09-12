using Basket.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basket.Api.Persistence.Configurations;

internal sealed class CustomerBasketConfiguration : IEntityTypeConfiguration<CustomerBasket>
{
    public void Configure(EntityTypeBuilder<CustomerBasket> builder)
    {
        builder.ToTable("Baskets");

        // The user id is the primary key. No surrogate, no separate unique
        // index - "one basket per user" is enforced by the table's own key.
        builder.HasKey(basket => basket.UserId);

        builder.Property(basket => basket.UserId)
            .ValueGeneratedNever();

        builder.Property(basket => basket.CreatedAtUtc)
            .IsRequired();

        // ------------------------------------------------------------------
        // THE BACKING FIELD - the piece most people get wrong.
        //
        // CustomerBasket.Items is IReadOnlyCollection, with the real List in a
        // private _items field. EF Core has to be told to read and write the
        // FIELD rather than the property, or it cannot materialise items into
        // an aggregate that refuses to expose a mutable collection.
        //
        // Without these two lines you get a runtime error about the navigation
        // not being settable, and the usual "fix" is to give up and expose
        // ICollection publicly - which throws away the encapsulation that made
        // the aggregate worth having.
        // ------------------------------------------------------------------
        builder.Metadata
            .FindNavigation(nameof(CustomerBasket.Items))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(basket => basket.Items)
            .HasField("_items")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // TotalQuantity and TotalAmount are computed properties on the
        // aggregate. They are NOT columns - EF would try to map them if we let
        // it, and then a basket total could disagree with its own line items.
        // Derived data belongs in code, not in a column nobody updates.
        builder.Ignore(basket => basket.TotalQuantity);
        builder.Ignore(basket => basket.TotalAmount);
    }
}
