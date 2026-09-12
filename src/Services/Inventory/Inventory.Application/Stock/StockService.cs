using Inventory.Application.Abstractions;
using Inventory.Application.Exceptions;
using Inventory.Application.Stock.Dtos;
using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Inventory.Application.Stock;

public sealed class StockService : IStockService
{
    private readonly IInventoryDbContext _dbContext;
    private readonly ILogger<StockService> _logger;

    public StockService(IInventoryDbContext dbContext, ILogger<StockService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<InventoryItemResponse?> GetAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        var item = await _dbContext.InventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.ProductId == productId, cancellationToken);

        return item is null ? null : ToResponse(item);
    }

    public async Task<InventoryItemResponse> AdjustAsync(
        StockAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        // -----------------------------------------------------------------
        // A DECISION WORTH DEFENDING: we do NOT ask Catalog whether this
        // product exists.
        //
        // Basket does exactly that before adding an item, and it was the right
        // call there - it closed a real exploit, because the price came from
        // the client. Here there is nothing to exploit. Adjusting stock for a
        // product id that does not exist is a data-entry error by a trusted
        // administrator, and the cost of preventing it would be that nobody can
        // receive a delivery while Catalog is restarting.
        //
        // "Check with the owning service" is not a rule to apply everywhere.
        // It is a trade of availability for consistency, and you make it where
        // the consistency is worth more than the availability.
        // -----------------------------------------------------------------
        var item = await _dbContext.InventoryItems
            .FirstOrDefaultAsync(candidate => candidate.ProductId == request.ProductId, cancellationToken);

        if (item is null)
        {
            if (request.QuantityChange < 0)
            {
                // You cannot write off stock you never received.
                throw new UnknownProductException(request.ProductId);
            }

            // First delivery of a new product: the stock record is born here.
            item = InventoryItem.Create(request.ProductId, request.QuantityChange);
            _dbContext.InventoryItems.Add(item);
        }
        else
        {
            item.Adjust(request.QuantityChange);
        }

        await SaveWithConcurrencyCheckAsync(cancellationToken);

        _logger.LogInformation(
            "Stock for product {ProductId} adjusted by {QuantityChange} ({Reason}). Now {Available} available, {Reserved} reserved",
            item.ProductId,
            request.QuantityChange,
            request.Reason,
            item.AvailableQuantity,
            item.ReservedQuantity);

        return ToResponse(item);
    }

    /// <summary>
    /// Saves, turning EF's concurrency exception into one the API layer can map
    /// to a 409.
    ///
    /// Note the catch is HERE, in Application, and not in the controller: the
    /// controller should not know that DbUpdateConcurrencyException exists, any
    /// more than it knows what SQL is. This method is the boundary where a
    /// persistence failure becomes a business outcome.
    /// </summary>
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
                "Concurrency conflict while updating stock - another request won the race");

            throw new StockConcurrencyConflictException();
        }
    }

    private static InventoryItemResponse ToResponse(InventoryItem item)
    {
        return new InventoryItemResponse(
            item.ProductId,
            item.AvailableQuantity,
            item.ReservedQuantity,
            item.TotalQuantity,
            item.Version,
            item.UpdatedAtUtc);
    }
}
