using System.ComponentModel.DataAnnotations;

namespace Inventory.Application.Reservations.Dtos;

/// <summary>The body of POST /api/inventory/reservations.</summary>
public sealed class CreateReservationRequest
{
    /// <summary>
    /// The caller's identifier for whatever this hold is for. Ordering will
    /// pass its order id here in Phase 8.
    /// </summary>
    public Guid ReferenceId { get; init; }

    /// <summary>
    /// All the lines, reserved together or not at all. MinLength(1) because a
    /// reservation for nothing is a request somebody built wrong.
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "A reservation must contain at least one line.")]
    [MaxLength(50)]
    public IReadOnlyList<ReservationLineRequest> Lines { get; init; } = [];
}

public sealed class ReservationLineRequest
{
    public Guid ProductId { get; init; }

    [Range(1, 10_000)]
    public int Quantity { get; init; }
}

public sealed record ReservationResponse(
    Guid Id,
    Guid ReferenceId,
    string Status,
    IReadOnlyList<ReservationLineResponse> Lines,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record ReservationLineResponse(Guid ProductId, int Quantity);
