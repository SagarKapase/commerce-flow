using Inventory.Domain.Entities;
using Inventory.Domain.Exceptions;
using Shouldly;

namespace Inventory.UnitTests;

/// <summary>
/// Tests for the reservation state machine.
///
/// A state machine is the ideal thing to unit-test, because "which transitions
/// are legal" is a finite, enumerable question. There are three states and two
/// operations, so there are six cases - and every one of them is covered below.
/// If you cannot list the cases, you do not yet understand the machine.
/// </summary>
public sealed class InventoryReservationTests
{
    private static readonly Guid ReferenceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProductA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ProductB = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static InventoryReservation NewReservation() =>
        InventoryReservation.Create(ReferenceId, [(ProductA, 2), (ProductB, 1)]);

    [Fact]
    public void A_new_reservation_is_held()
    {
        var reservation = NewReservation();

        reservation.Status.ShouldBe(ReservationStatus.Held);
        reservation.Lines.Count.ShouldBe(2);
        reservation.CompletedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void Held_can_be_confirmed()
    {
        var reservation = NewReservation();

        var changed = reservation.Confirm();

        changed.ShouldBeTrue();
        reservation.Status.ShouldBe(ReservationStatus.Confirmed);
        reservation.CompletedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public void Held_can_be_released()
    {
        var reservation = NewReservation();

        var changed = reservation.Release();

        changed.ShouldBeTrue();
        reservation.Status.ShouldBe(ReservationStatus.Released);
    }

    [Fact]
    public void Confirming_twice_is_safe_and_reports_that_nothing_changed()
    {
        var reservation = NewReservation();
        reservation.Confirm();

        var changed = reservation.Confirm();

        // false is the signal the service uses to skip touching stock a second
        // time. This is what makes at-least-once message delivery survivable
        // without a deduplication table - Phase 10 depends on it.
        changed.ShouldBeFalse();
        reservation.Status.ShouldBe(ReservationStatus.Confirmed);
    }

    [Fact]
    public void Releasing_twice_is_safe_and_reports_that_nothing_changed()
    {
        var reservation = NewReservation();
        reservation.Release();

        var changed = reservation.Release();

        // Release is a saga's COMPENSATING action. Compensation runs when
        // things have already gone wrong, which is exactly when retries happen,
        // so a second call must not fail.
        changed.ShouldBeFalse();
        reservation.Status.ShouldBe(ReservationStatus.Released);
    }

    [Fact]
    public void A_released_reservation_cannot_be_confirmed()
    {
        var reservation = NewReservation();
        reservation.Release();

        var exception = Should.Throw<InvalidReservationStateException>(() => reservation.Confirm());

        exception.CurrentStatus.ShouldBe(ReservationStatus.Released);

        // Released is not "not yet confirmed" - it is a decision that went the
        // other way. Overriding it would ship goods for a cancelled order.
        reservation.Status.ShouldBe(ReservationStatus.Released);
    }

    [Fact]
    public void A_confirmed_reservation_cannot_be_released()
    {
        var reservation = NewReservation();
        reservation.Confirm();

        Should.Throw<InvalidReservationStateException>(() => reservation.Release());

        // The goods have shipped. Putting them back is a refund - a different
        // business process, not two columns quietly moving.
        reservation.Status.ShouldBe(ReservationStatus.Confirmed);
    }

    [Fact]
    public void A_reservation_cannot_list_the_same_product_twice()
    {
        // The composite key would reject this at the database anyway. Catching
        // it in the domain turns a provider-specific constraint error into a
        // sentence that says what the caller did wrong.
        Should.Throw<ArgumentException>(() =>
            InventoryReservation.Create(ReferenceId, [(ProductA, 1), (ProductA, 2)]));
    }

    [Fact]
    public void A_reservation_must_have_at_least_one_line()
    {
        Should.Throw<ArgumentException>(() =>
            InventoryReservation.Create(ReferenceId, []));
    }

    [Fact]
    public void Reservation_quantities_must_be_positive()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            InventoryReservation.Create(ReferenceId, [(ProductA, 0)]));
    }
}
