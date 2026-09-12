using Inventory.Application.Abstractions;
using Inventory.Application.Exceptions;
using Inventory.Application.Reservations.Dtos;
using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Inventory.Application.Reservations;

public sealed class ReservationService : IReservationService
{
    private readonly IInventoryDbContext _dbContext;
    private readonly ILogger<ReservationService> _logger;

    public ReservationService(IInventoryDbContext dbContext, ILogger<ReservationService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ReservationResponse?> GetAsync(
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        var reservation = await _dbContext.Reservations
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == reservationId, cancellationToken);

        return reservation is null ? null : ToResponse(reservation);
    }

    public async Task<ReservationResponse> CreateAsync(
        CreateReservationRequest request,
        CancellationToken cancellationToken)
    {
        var requestedProductIds = request.Lines
            .Select(line => line.ProductId)
            .ToList();

        // One query for every product involved, not one per line. Reserving a
        // ten-line order should cost one SELECT, not ten - the N+1 problem is
        // easiest to avoid at the moment you write the loop.
        var items = await _dbContext.InventoryItems
            .Where(item => requestedProductIds.Contains(item.ProductId))
            .ToDictionaryAsync(item => item.ProductId, cancellationToken);

        // Check EVERYTHING before changing ANYTHING. Validating as we go would
        // mutate the first two items and then discover the third is unknown -
        // and although SaveChanges would never be called, the tracked entities
        // would be in a state no longer matching the database, which is a
        // genuinely confusing thing to debug.
        foreach (var productId in requestedProductIds)
        {
            if (!items.ContainsKey(productId))
            {
                throw new UnknownProductException(productId);
            }
        }

        var reservation = InventoryReservation.Create(
            request.ReferenceId,
            request.Lines
                .Select(line => (line.ProductId, line.Quantity))
                .ToList());

        // --------------------------------------------------------------
        // ALL OR NOTHING.
        //
        // Every Reserve() below runs against a tracked entity, so nothing has
        // touched the database yet. If line three throws InsufficientStock, the
        // exception leaves this method, SaveChangesAsync is never reached, and
        // the two successful reservations simply never happened.
        //
        // One SaveChangesAsync means one transaction. That is the guarantee
        // Inventory can make for itself inside its own database - and the
        // reason a reservation covers many products instead of one.
        //
        // It is also worth noticing what we CANNOT guarantee this way: the same
        // atomicity across Ordering, Inventory and Payment. Three databases, no
        // shared transaction. That gap is what a saga exists to fill, and it is
        // Phase 11.
        // --------------------------------------------------------------
        foreach (var line in reservation.Lines)
        {
            items[line.ProductId].Reserve(line.Quantity);
        }

        _dbContext.Reservations.Add(reservation);

        await SaveWithConcurrencyCheckAsync(cancellationToken);

        _logger.LogInformation(
            "Reservation {ReservationId} created for reference {ReferenceId} across {LineCount} line(s)",
            reservation.Id,
            reservation.ReferenceId,
            reservation.Lines.Count);

        return ToResponse(reservation);
    }

    public async Task<ReservationResponse?> ConfirmAsync(
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        var reservation = await LoadForUpdateAsync(reservationId, cancellationToken);

        if (reservation is null)
        {
            return null;
        }

        // The aggregate decides whether anything changed. If it was already
        // confirmed, Confirm() returns false and we deliberately do NOT touch
        // stock - applying the same confirmation twice would decrement reserved
        // quantities that were already decremented, and the numbers would drift
        // downwards every redelivery.
        if (!reservation.Confirm())
        {
            _logger.LogInformation(
                "Reservation {ReservationId} was already confirmed; nothing to do",
                reservationId);

            return ToResponse(reservation);
        }

        var items = await LoadItemsForAsync(reservation, cancellationToken);

        foreach (var line in reservation.Lines)
        {
            items[line.ProductId].ConfirmReservation(line.Quantity);
        }

        await SaveWithConcurrencyCheckAsync(cancellationToken);

        _logger.LogInformation("Reservation {ReservationId} confirmed", reservationId);

        return ToResponse(reservation);
    }

    public async Task<ReservationResponse?> ReleaseAsync(
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        var reservation = await LoadForUpdateAsync(reservationId, cancellationToken);

        if (reservation is null)
        {
            return null;
        }

        if (!reservation.Release())
        {
            _logger.LogInformation(
                "Reservation {ReservationId} was already released; nothing to do",
                reservationId);

            return ToResponse(reservation);
        }

        var items = await LoadItemsForAsync(reservation, cancellationToken);

        foreach (var line in reservation.Lines)
        {
            items[line.ProductId].Release(line.Quantity);
        }

        await SaveWithConcurrencyCheckAsync(cancellationToken);

        _logger.LogInformation("Reservation {ReservationId} released", reservationId);

        return ToResponse(reservation);
    }

    private Task<InventoryReservation?> LoadForUpdateAsync(
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        return _dbContext.Reservations
            .Include(reservation => reservation.Lines)
            .FirstOrDefaultAsync(reservation => reservation.Id == reservationId, cancellationToken);
    }

    private async Task<Dictionary<Guid, InventoryItem>> LoadItemsForAsync(
        InventoryReservation reservation,
        CancellationToken cancellationToken)
    {
        var productIds = reservation.Lines
            .Select(line => line.ProductId)
            .ToList();

        return await _dbContext.InventoryItems
            .Where(item => productIds.Contains(item.ProductId))
            .ToDictionaryAsync(item => item.ProductId, cancellationToken);
    }

    private async Task SaveWithConcurrencyCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogWarning(
                exception,
                "Concurrency conflict while reserving stock - another request won the race");

            throw new StockConcurrencyConflictException();
        }
    }

    private static ReservationResponse ToResponse(InventoryReservation reservation)
    {
        return new ReservationResponse(
            reservation.Id,
            reservation.ReferenceId,
            reservation.Status.ToString(),
            reservation.Lines
                .Select(line => new ReservationLineResponse(line.ProductId, line.Quantity))
                .ToList(),
            reservation.CreatedAtUtc,
            reservation.CompletedAtUtc);
    }
}
