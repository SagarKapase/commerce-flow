namespace Notification.Worker.IntegrationEvents;

/// <summary>
/// ============ NOTIFICATION'S OWN COPY OF THE CONTRACTS ============
///
/// These are DUPLICATED from Ordering.Infrastructure.Messaging, and that is
/// the deliberate architectural choice - the same one made for the HTTP DTOs
/// in Phase 5 (CatalogProduct) and Phase 8 (BasketSnapshotResponse).
///
/// Look at what Ordering publishes versus what is declared here:
///
///   Ordering's OrderPlacedIntegrationEvent has EventId, OccurredAtUtc,
///   OrderId, CustomerId, TotalAmount, ItemCount and InventoryReservationId.
///
///   This one has four fields, because writing an email needs four fields.
///   InventoryReservationId is not here. It is not missing - Notification has
///   no use for it, so it is never deserialised.
///
/// TOLERANT READER. System.Text.Json silently ignores JSON properties with no
/// matching member, so Ordering can add fields to its event and this service
/// keeps working untouched and undeployed. Only removing or renaming one of
/// THESE four is a breaking change - a small, explicit list rather than
/// "everything on the wire".
///
/// The shared-package alternative would give compile-time safety and take away
/// exactly that independence. See MessagingServiceCollectionExtensions for the
/// full argument.
///
/// Note also: no base class, no IntegrationEvent inheritance. A consumer does
/// not need the publisher's abstractions, only its JSON.
/// ==================================================================
/// </summary>
public sealed record OrderPlacedEvent(
    Guid OrderId,
    Guid CustomerId,
    decimal TotalAmount,
    int ItemCount);

public sealed record OrderConfirmedEvent(
    Guid OrderId,
    Guid CustomerId,
    decimal TotalAmount);

public sealed record OrderPaymentFailedEvent(
    Guid OrderId,
    Guid CustomerId,
    decimal TotalAmount,
    string? FailureReason);
