using Ordering.Domain.Entities;
using Ordering.Domain.Exceptions;
using Ordering.Domain.ValueObjects;
using Shouldly;

namespace Ordering.UnitTests;

/// <summary>
/// The Order state machine, exhaustively.
///
/// Seven states and six operations is forty-two combinations, which sounds like
/// too many to test until you notice that four of the states are terminal and
/// behave identically. What is left is small enough to enumerate, and
/// enumerating it is the only way to know the machine has no holes.
///
/// Every test below is a sentence from the business process. If a rule cannot
/// be written as one of these, it probably is not a rule yet - just an
/// assumption somebody is carrying around.
/// </summary>
public sealed class OrderTests
{
    private static readonly Guid CustomerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherCustomerId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid ProductA = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProductB = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly Address AnyAddress =
        new("1 High Street", "Bengaluru", "560001", "India");

    private static Order NewOrder() => Order.Create(
        CustomerId,
        AnyAddress,
        [(ProductA, "Mechanical Keyboard", 8999.00m, 2),
         (ProductB, "Wireless Mouse", 1500.50m, 1)]);

    // ---------------------------------------------------------------
    // Creation
    // ---------------------------------------------------------------

    [Fact]
    public void A_new_order_is_pending_and_owned_by_its_customer()
    {
        var order = NewOrder();

        order.Status.ShouldBe(OrderStatus.Pending);
        order.CustomerId.ShouldBe(CustomerId);
        order.Items.Count.ShouldBe(2);
        order.InventoryReservationId.ShouldBeNull();
        order.PaymentId.ShouldBeNull();
        order.FailureReason.ShouldBeNull();
    }

    [Fact]
    public void The_total_is_the_sum_of_the_lines()
    {
        var order = NewOrder();

        // 8999.00 x 2 + 1500.50 x 1
        order.TotalAmount.ShouldBe(19_498.50m);
    }

    [Fact]
    public void The_total_is_exact_decimal_arithmetic()
    {
        var order = Order.Create(CustomerId, AnyAddress, [(ProductA, "Penny sweet", 0.10m, 3)]);

        // 0.30m exactly. The same sum in double would be 0.30000000000000004,
        // which is why money is decimal all the way through and stored as an
        // integer count of minor units.
        order.TotalAmount.ShouldBe(0.30m);
    }

    [Fact]
    public void The_shipping_address_is_a_value_object_with_value_equality()
    {
        var order = NewOrder();

        // Two Address instances with the same contents ARE the same address.
        // An entity would never behave this way - that is exactly the
        // distinction.
        order.ShippingAddress.ShouldBe(new Address("1 High Street", "Bengaluru", "560001", "India"));
    }

    [Fact]
    public void An_order_must_have_at_least_one_item()
    {
        Should.Throw<ArgumentException>(() => Order.Create(CustomerId, AnyAddress, []));
    }

    [Fact]
    public void An_order_cannot_list_the_same_product_twice()
    {
        // Two of the same thing is one line with a quantity of two. The
        // composite key would reject the alternative anyway; catching it here
        // produces a sentence instead of a constraint violation.
        Should.Throw<ArgumentException>(() => Order.Create(
            CustomerId, AnyAddress,
            [(ProductA, "Keyboard", 100m, 1), (ProductA, "Keyboard again", 100m, 1)]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Order_quantities_must_be_positive(int quantity)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Order.Create(
            CustomerId, AnyAddress, [(ProductA, "Keyboard", 100m, quantity)]));
    }

    [Fact]
    public void Order_prices_must_be_positive()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Order.Create(
            CustomerId, AnyAddress, [(ProductA, "Free keyboard", 0m, 1)]));
    }

    // ---------------------------------------------------------------
    // The happy path, one transition at a time
    // ---------------------------------------------------------------

    [Fact]
    public void The_whole_happy_path_runs_pending_to_confirmed()
    {
        var order = NewOrder();
        var reservationId = Guid.CreateVersion7();
        var paymentId = Guid.CreateVersion7();

        order.MarkInventoryReserved(reservationId);
        order.Status.ShouldBe(OrderStatus.InventoryReserved);
        order.InventoryReservationId.ShouldBe(reservationId);

        order.StartPayment(paymentId);
        order.Status.ShouldBe(OrderStatus.PaymentProcessing);
        order.PaymentId.ShouldBe(paymentId);

        order.Confirm().ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Confirmed);
    }

    [Fact]
    public void The_unhappy_path_ends_in_payment_failed_with_a_reason()
    {
        var order = NewOrder();
        order.MarkInventoryReserved(Guid.CreateVersion7());
        order.StartPayment(Guid.CreateVersion7());

        order.MarkPaymentFailed("Card declined").ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.PaymentFailed);
        order.FailureReason.ShouldBe("Card declined");

        // The order does NOT go back to Pending. Compensation undoes side
        // effects - in Phase 11, releasing the inventory hold - but it does not
        // rewind time. The order ended, and the record says how.
    }

    // ---------------------------------------------------------------
    // Skipping steps
    // ---------------------------------------------------------------

    [Fact]
    public void Payment_cannot_start_before_inventory_is_reserved()
    {
        var order = NewOrder();

        // Taking money for goods that might not exist is the one ordering
        // mistake customers never forgive.
        var exception = Should.Throw<InvalidOrderStateTransitionException>(
            () => order.StartPayment(Guid.CreateVersion7()));

        exception.CurrentStatus.ShouldBe(OrderStatus.Pending);
        order.Status.ShouldBe(OrderStatus.Pending);
    }

    [Fact]
    public void An_order_cannot_be_confirmed_without_a_payment_in_flight()
    {
        var order = NewOrder();
        order.MarkInventoryReserved(Guid.CreateVersion7());

        Should.Throw<InvalidOrderStateTransitionException>(() => order.Confirm());

        order.Status.ShouldBe(OrderStatus.InventoryReserved);
    }

    [Fact]
    public void Inventory_cannot_be_reserved_twice()
    {
        var order = NewOrder();
        order.MarkInventoryReserved(Guid.CreateVersion7());

        Should.Throw<InvalidOrderStateTransitionException>(
            () => order.MarkInventoryReserved(Guid.CreateVersion7()));
    }

    // ---------------------------------------------------------------
    // Cancellation - two states allow it, five do not
    // ---------------------------------------------------------------

    [Fact]
    public void A_pending_order_can_be_cancelled()
    {
        var order = NewOrder();

        order.CanBeCancelled.ShouldBeTrue();
        order.Cancel();

        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.CanBeCancelled.ShouldBeFalse();
    }

    [Fact]
    public void An_order_with_stock_reserved_can_still_be_cancelled()
    {
        var order = NewOrder();
        order.MarkInventoryReserved(Guid.CreateVersion7());

        order.CanBeCancelled.ShouldBeTrue();
        order.Cancel();

        order.Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public void An_order_being_paid_for_cannot_be_cancelled()
    {
        var order = NewOrder();
        order.MarkInventoryReserved(Guid.CreateVersion7());
        order.StartPayment(Guid.CreateVersion7());

        // Money may already be moving. A cancel racing a charge is how you
        // refund something that never completed, or ship something you
        // refunded.
        order.CanBeCancelled.ShouldBeFalse();
        Should.Throw<InvalidOrderStateTransitionException>(() => order.Cancel());

        order.Status.ShouldBe(OrderStatus.PaymentProcessing);
    }

    [Fact]
    public void A_confirmed_order_cannot_be_cancelled()
    {
        var order = NewOrder();
        order.MarkInventoryReserved(Guid.CreateVersion7());
        order.StartPayment(Guid.CreateVersion7());
        order.Confirm();

        // Paid, and the stock is consumed. Undoing that is a refund and a
        // return - a different business process, not one column changing.
        Should.Throw<InvalidOrderStateTransitionException>(() => order.Cancel());

        order.Status.ShouldBe(OrderStatus.Confirmed);
    }

    [Fact]
    public void A_cancelled_order_cannot_be_cancelled_again()
    {
        var order = NewOrder();
        order.Cancel();

        Should.Throw<InvalidOrderStateTransitionException>(() => order.Cancel());
    }

    [Fact]
    public void A_rejected_order_cannot_be_cancelled()
    {
        var order = NewOrder();
        order.Reject("Insufficient stock");

        order.Status.ShouldBe(OrderStatus.Rejected);
        order.FailureReason.ShouldBe("Insufficient stock");
        Should.Throw<InvalidOrderStateTransitionException>(() => order.Cancel());
    }

    // ---------------------------------------------------------------
    // Idempotency - the same reason as Inventory's reservation
    // ---------------------------------------------------------------

    [Fact]
    public void Confirming_twice_is_safe_and_reports_that_nothing_changed()
    {
        var order = NewOrder();
        order.MarkInventoryReserved(Guid.CreateVersion7());
        order.StartPayment(Guid.CreateVersion7());
        order.Confirm();

        // A broker guaranteeing at-least-once delivery WILL send "payment
        // succeeded" twice eventually. false is the signal the caller uses to
        // skip the follow-up work rather than apply it a second time.
        order.Confirm().ShouldBeFalse();
        order.Status.ShouldBe(OrderStatus.Confirmed);
    }

    [Fact]
    public void Marking_payment_failed_twice_is_safe()
    {
        var order = NewOrder();
        order.MarkInventoryReserved(Guid.CreateVersion7());
        order.StartPayment(Guid.CreateVersion7());
        order.MarkPaymentFailed("Card declined");

        order.MarkPaymentFailed("Card declined again").ShouldBeFalse();

        // The FIRST reason is kept. A redelivered message must not overwrite
        // the record of what actually happened.
        order.FailureReason.ShouldBe("Card declined");
    }

    [Fact]
    public void A_cancelled_order_cannot_then_be_confirmed()
    {
        var order = NewOrder();
        order.Cancel();

        // The single most important thing this state machine prevents: an order
        // the customer called off quietly becoming a shipped, paid order
        // because a late message arrived.
        Should.Throw<InvalidOrderStateTransitionException>(() => order.Confirm());

        order.Status.ShouldBe(OrderStatus.Cancelled);
    }

    // ---------------------------------------------------------------
    // Ownership is a property of the order, not of the request
    // ---------------------------------------------------------------

    [Fact]
    public void The_customer_id_is_fixed_at_creation()
    {
        var order = NewOrder();

        order.CustomerId.ShouldBe(CustomerId);
        order.CustomerId.ShouldNotBe(OtherCustomerId);

        // There is no setter and no method to change it. An order does not move
        // between customers, so the type does not offer a way to try.
    }
}
