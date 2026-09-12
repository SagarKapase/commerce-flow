using Inventory.Application.Reservations.Dtos;

namespace Inventory.Application.Reservations;

public interface IReservationService
{
    /// <returns>The reservation, or <c>null</c> if no reservation has that id.</returns>
    Task<ReservationResponse?> GetAsync(Guid reservationId, CancellationToken cancellationToken);

    /// <summary>
    /// Holds stock across every line, or none of them.
    /// </summary>
    Task<ReservationResponse> CreateAsync(
        CreateReservationRequest request,
        CancellationToken cancellationToken);

    /// <summary>Turns a hold into a sale. Safe to call twice.</summary>
    /// <returns><c>null</c> if no reservation has that id.</returns>
    Task<ReservationResponse?> ConfirmAsync(Guid reservationId, CancellationToken cancellationToken);

    /// <summary>Returns held stock to the available pool. Safe to call twice.</summary>
    /// <returns><c>null</c> if no reservation has that id.</returns>
    Task<ReservationResponse?> ReleaseAsync(Guid reservationId, CancellationToken cancellationToken);
}
