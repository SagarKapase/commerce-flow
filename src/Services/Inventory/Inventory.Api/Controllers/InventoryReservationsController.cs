using Inventory.Application.Reservations;
using Inventory.Application.Reservations.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>
/// The reservation workflow - the machinery an order runs through.
///
/// ============ AN OPEN QUESTION, DELIBERATELY LEFT OPEN ============
/// These endpoints are [Authorize] with no role: any authenticated caller.
/// That is looser than it should be, and it is a placeholder rather than a
/// decision.
///
/// In Phase 8 the Ordering service will call these, and service-to-service
/// authentication has three real answers:
///
///   1. Forward the caller's token ("on-behalf-of"). Simple, and Inventory
///      knows which customer the reservation is ultimately for. But every
///      downstream service now accepts customer tokens, and a token stolen from
///      a browser reaches all of them.
///   2. A service credential (OAuth client credentials). Ordering gets its own
///      identity and its own token, so Inventory can require the "ordering"
///      client specifically. Correct, and needs an identity provider that
///      issues machine tokens.
///   3. Network-level trust - mTLS, a service mesh, or simply not exposing
///      these endpoints outside the cluster. Common in practice, and the reason
///      real internal APIs are often less protected than they look.
///
/// Picking one now would be guessing at a problem we have not met. It is
/// flagged here so it does not silently become "the way it has always been" -
/// which is how most over-permissive internal APIs come to exist.
/// ==================================================================
/// </summary>
[ApiController]
[Route("api/inventory/reservations")]
[Produces("application/json")]
[Authorize]
public sealed class InventoryReservationsController : ControllerBase
{
    private readonly IReservationService _reservationService;

    public InventoryReservationsController(IReservationService reservationService)
    {
        _reservationService = reservationService;
    }

    /// <summary>Gets one reservation.</summary>
    /// <remarks>
    /// GET /api/inventory/reservations/{id}
    /// Responses: 200 OK, 401, 404 Not Found
    /// </remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReservationResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var reservation = await _reservationService.GetAsync(id, cancellationToken);

        if (reservation is null)
        {
            return NotFound();
        }

        return Ok(reservation);
    }

    /// <summary>Holds stock for every line, or none of them.</summary>
    /// <remarks>
    /// POST /api/inventory/reservations
    /// Body: CreateReservationRequest
    /// Responses:
    ///   201 Created - Location points at GET /api/inventory/reservations/{id}
    ///   400 Bad Request - malformed, or a product with no stock record
    ///   401 Unauthorized
    ///   409 Conflict - not enough stock, or another request won the race
    ///
    /// Breakpoints: InventoryReservationsController.Create
    ///              -> ReservationService.CreateAsync
    ///              -> InventoryItem.Reserve
    /// Watch: items dictionary, item.AvailableQuantity BEFORE and AFTER
    ///        Reserve, item.Version, then step over SaveChangesAsync and read
    ///        the generated UPDATE in the console.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReservationResponse>> Create(
        CreateReservationRequest request,
        CancellationToken cancellationToken)
    {
        var reservation = await _reservationService.CreateAsync(request, cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = reservation.Id },
            reservation);
    }

    /// <summary>Turns a hold into a sale.</summary>
    /// <remarks>
    /// POST /api/inventory/reservations/{id}/confirm
    /// Responses: 200 OK, 401, 404 Not Found,
    ///            409 Conflict (the reservation was released)
    ///
    /// POST on a sub-resource, not PUT on the reservation. "Confirm" is a
    /// business operation with its own rules, not a field somebody sets - the
    /// same reason Orders will have /cancel instead of an editable status.
    ///
    /// SAFE TO CALL TWICE. The second call returns 200 with the same body and
    /// changes no stock. That matters more than it looks: in Phase 10 a message
    /// broker guarantees AT LEAST once delivery, so "confirm this reservation"
    /// will sometimes arrive twice, and the second one must not decrement stock
    /// again.
    /// </remarks>
    [HttpPost("{id:guid}/confirm")]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReservationResponse>> Confirm(
        Guid id,
        CancellationToken cancellationToken)
    {
        var reservation = await _reservationService.ConfirmAsync(id, cancellationToken);

        if (reservation is null)
        {
            return NotFound();
        }

        return Ok(reservation);
    }

    /// <summary>Returns held stock to the available pool.</summary>
    /// <remarks>
    /// POST /api/inventory/reservations/{id}/release
    /// Responses: 200 OK, 401, 404 Not Found,
    ///            409 Conflict (the reservation was already confirmed)
    ///
    /// THIS IS A COMPENSATING ACTION - the undo half of the saga we build in
    /// Phase 11. It runs when something has already gone wrong (payment
    /// declined, order cancelled), which is exactly when retries happen, so it
    /// is idempotent by design: releasing twice succeeds and moves stock once.
    ///
    /// A compensating action that can fail on retry is a saga that cannot
    /// finish unwinding, and an order stuck forever in a half-done state.
    /// </remarks>
    [HttpPost("{id:guid}/release")]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReservationResponse>> Release(
        Guid id,
        CancellationToken cancellationToken)
    {
        var reservation = await _reservationService.ReleaseAsync(id, cancellationToken);

        if (reservation is null)
        {
            return NotFound();
        }

        return Ok(reservation);
    }
}
