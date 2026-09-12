using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Abstractions;

/// <summary>
/// The application layer's view of the database - same pattern as Catalog's
/// ICatalogDbContext, and for the same reason: Application must not reference
/// Infrastructure, but it still needs to query and save.
/// </summary>
public interface IInventoryDbContext
{
    DbSet<InventoryItem> InventoryItems { get; }

    DbSet<InventoryReservation> Reservations { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
