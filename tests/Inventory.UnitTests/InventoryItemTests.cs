using Inventory.Domain.Entities;
using Inventory.Domain.Exceptions;
using Shouldly;

namespace Inventory.UnitTests;

/// <summary>
/// THE FIRST TESTS IN THIS PROJECT, and it is worth saying why they arrive now
/// rather than in Phase 1.
///
/// Catalog had almost no logic to test. Its rules were "the name is required"
/// and "the SKU is unique" - one enforced by a validation attribute, the other
/// by a database index. Testing them would have tested the framework.
///
/// InventoryItem is different: it holds an invariant across two fields that has
/// to survive four different operations. That is arithmetic somebody can get
/// wrong, which makes it worth pinning down.
///
/// Notice what these tests need: nothing. No database, no mocks, no
/// WebApplicationFactory, no DI container. Every one runs in microseconds
/// against a plain object. THAT is the payoff of a Domain project that
/// references nothing - and the reason "is this testable without
/// infrastructure?" is a good question to ask about any piece of logic.
/// </summary>
public sealed class InventoryItemTests
{
    private static readonly Guid ProductId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Create_starts_with_everything_available_and_nothing_reserved()
    {
        var item = InventoryItem.Create(ProductId, 10);

        item.AvailableQuantity.ShouldBe(10);
        item.ReservedQuantity.ShouldBe(0);
        item.TotalQuantity.ShouldBe(10);
        item.Version.ShouldBe(1);
    }

    [Fact]
    public void Reserve_moves_units_between_buckets_without_changing_the_total()
    {
        var item = InventoryItem.Create(ProductId, 10);

        item.Reserve(3);

        item.AvailableQuantity.ShouldBe(7);
        item.ReservedQuantity.ShouldBe(3);

        // The central rule of this service: reserving does not consume stock.
        // The goods are still on the shelf, they are just spoken for.
        item.TotalQuantity.ShouldBe(10);
    }

    [Fact]
    public void Reserve_increments_the_concurrency_token()
    {
        var item = InventoryItem.Create(ProductId, 10);

        item.Reserve(1);

        // If this ever fails, optimistic concurrency silently stops working:
        // the UPDATE would still carry "WHERE Version = @original", but the
        // value would never move, so no conflict could ever be detected.
        item.Version.ShouldBe(2);
    }

    [Fact]
    public void Reserve_more_than_available_throws_and_changes_nothing()
    {
        var item = InventoryItem.Create(ProductId, 2);

        var exception = Should.Throw<InsufficientStockException>(() => item.Reserve(3));

        exception.Requested.ShouldBe(3);
        exception.Available.ShouldBe(2);

        // The entity must be untouched after a rejected operation. An aggregate
        // that half-applies a failed change is worse than one that throws,
        // because the damage is invisible.
        item.AvailableQuantity.ShouldBe(2);
        item.ReservedQuantity.ShouldBe(0);
    }

    [Fact]
    public void Reserve_can_take_exactly_everything_that_is_left()
    {
        var item = InventoryItem.Create(ProductId, 5);

        item.Reserve(5);

        item.AvailableQuantity.ShouldBe(0);
        item.ReservedQuantity.ShouldBe(5);
    }

    [Fact]
    public void Release_puts_reserved_units_back_on_the_shelf()
    {
        var item = InventoryItem.Create(ProductId, 10);
        item.Reserve(4);

        item.Release(4);

        item.AvailableQuantity.ShouldBe(10);
        item.ReservedQuantity.ShouldBe(0);
        item.TotalQuantity.ShouldBe(10);
    }

    [Fact]
    public void Confirm_is_the_only_operation_that_reduces_total_stock()
    {
        var item = InventoryItem.Create(ProductId, 10);
        item.Reserve(4);

        item.ConfirmReservation(4);

        item.ReservedQuantity.ShouldBe(0);
        item.AvailableQuantity.ShouldBe(6);

        // The goods have shipped. This is the moment stock actually leaves.
        item.TotalQuantity.ShouldBe(6);
    }

    [Fact]
    public void Reserve_then_release_leaves_the_item_exactly_as_it_was()
    {
        var item = InventoryItem.Create(ProductId, 10);

        item.Reserve(6);
        item.Release(6);

        // A round trip must be lossless. If reserve-then-release ever leaked a
        // unit, stock would drift down by one for every abandoned checkout, and
        // nobody would notice until the shelf disagreed with the database.
        item.AvailableQuantity.ShouldBe(10);
        item.ReservedQuantity.ShouldBe(0);
    }

    [Fact]
    public void Adjust_cannot_take_stock_that_is_already_reserved()
    {
        var item = InventoryItem.Create(ProductId, 10);
        item.Reserve(8);

        // 2 available, 8 promised to somebody. Writing off 5 would have to come
        // out of the reserved units - overselling an order already accepted.
        Should.Throw<InsufficientStockException>(() => item.Adjust(-5));

        item.AvailableQuantity.ShouldBe(2);
        item.ReservedQuantity.ShouldBe(8);
    }

    [Fact]
    public void Adjust_adds_received_stock_to_available_only()
    {
        var item = InventoryItem.Create(ProductId, 4);
        item.Reserve(4);

        item.Adjust(10);

        item.AvailableQuantity.ShouldBe(10);
        item.ReservedQuantity.ShouldBe(4);
        item.TotalQuantity.ShouldBe(14);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reserve_rejects_quantities_below_one(int quantity)
    {
        var item = InventoryItem.Create(ProductId, 10);

        Should.Throw<ArgumentOutOfRangeException>(() => item.Reserve(quantity));
    }
}
