using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Application.Abstractions;

/// <summary>
/// The application layer's view of the database.
///
/// WHY THIS EXISTS (and why it is not a repository):
///
/// Problem: application services need to query and save data, but
/// Catalog.Application must not reference Catalog.Infrastructure - otherwise
/// the dependency arrows point both ways and the layering means nothing.
///
/// Option A - a repository per entity (IProductRepository.GetAllAsync(), ...).
/// Every new filter or sort needs a new method, and IQueryable composition,
/// projections and paging all have to be re-invented behind the interface.
///
/// Option B - this: expose the DbSets through an interface the Application
/// layer owns and Infrastructure implements. Services keep full LINQ, EF Core
/// stays swappable at the composition root, and there is no repository to write.
///
/// The cost of Option B is honest and worth stating in an interview:
/// Catalog.Application now depends on EF Core's abstractions. It does NOT
/// depend on SQLite, on the connection string, or on the concrete DbContext -
/// and Catalog.Domain still depends on nothing at all, which is what actually
/// protects the business rules.
///
/// In Phase 7 the Ordering service will do the opposite - a hand-written
/// IOrderRepository - because an Order is loaded and saved as one aggregate
/// with invariants across its items. Comparing the two is the point.
/// </summary>
public interface ICatalogDbContext
{
    DbSet<Product> Products { get; }

    DbSet<Category> Categories { get; }

    /// <summary>
    /// Writes every tracked change in one database transaction and returns the
    /// number of affected rows. EF Core's DbContext is already a Unit of Work:
    /// nothing is sent to SQLite until this is called.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
