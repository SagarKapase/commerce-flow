using Ordering.Domain.Entities;

namespace Ordering.Application.Abstractions;

/// <summary>
/// ==================================================================
/// THE OPPOSITE DECISION FROM CATALOG - READ BOTH TOGETHER
/// ==================================================================
///
/// Catalog.Application defines ICatalogDbContext, which exposes DbSet&lt;T&gt;
/// and lets services compose LINQ freely. Ordering.Application defines this
/// instead: five named methods and no IQueryable anywhere.
///
/// Neither is "the right way". They are answers to different problems, and
/// being able to say why is worth more in an interview than either pattern.
///
/// WHY A REPOSITORY HERE:
///
///  1. Ordering.Application does not reference EF Core AT ALL. Open
///     Ordering.Application.csproj - one package, Logging.Abstractions.
///     Catalog.Application had to take a dependency on EF Core to have a
///     DbSet to expose. Here, the application layer depends on Domain and
///     nothing else, so its use cases could run against a document store, a
///     web service, or an in-memory list without a single edit.
///
///  2. The query set is CLOSED. There are exactly five things this service
///     ever needs from storage, and all five are below. Catalog's was open:
///     filter by search, by category, by price range, by active flag, in any
///     combination, plus whatever a future feature invents. A repository over
///     THAT would grow a method per combination, or grow an argument that is
///     secretly a query language.
///
///  3. An Order is loaded and saved as a WHOLE AGGREGATE - root, items, and
///     the owned address. With a DbSet exposed, any service method could write
///     `_db.OrderItems.Where(...)` and update a line without going through the
///     Order that is supposed to be enforcing the rules. The repository makes
///     the aggregate the only unit of work available.
///
/// WHY SaveChangesAsync IS ON THIS INTERFACE rather than a separate
/// IUnitOfWork: this repository IS the unit of work for the Order aggregate.
/// A second interface over the same DbContext would add a file and no
/// capability. You would split them the moment two different aggregates had to
/// be saved in one transaction - and that moment is worth waiting for.
/// </summary>
public interface IOrderRepository
{
    /// <summary>
    /// Loads one order with its items, TRACKED and ready to be modified.
    /// </summary>
    /// <returns><c>null</c> if no order has that id.</returns>
    Task<Order?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Every order a customer has placed, newest first. Read-only.</summary>
    Task<IReadOnlyList<Order>> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>One page of all orders, newest first. Read-only. Administrators.</summary>
    Task<(IReadOnlyList<Order> Orders, int TotalCount)> GetPageAsync(
        int page,
        int pageSize,
        OrderStatus? status,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stages a new order. Nothing is written until SaveChangesAsync -
    /// deliberately mirroring EF's own semantics, so the two halves of "create
    /// then commit" stay visible at the call site.
    /// </summary>
    void Add(Order order);

    /// <summary>Commits everything staged in this unit of work, in one transaction.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
