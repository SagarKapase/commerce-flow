using Microsoft.EntityFrameworkCore;
using Ordering.Application.Abstractions;
using Ordering.Domain.Entities;

namespace Ordering.Infrastructure.Persistence.Repositories;

/// <summary>
/// The EF Core implementation of IOrderRepository.
///
/// This is the ONLY file in the Ordering service that knows EF Core exists
/// outside of Program.cs and the configurations. Everything above it - the
/// service, the controller, the domain - would compile unchanged against a
/// completely different implementation.
///
/// It is also small, and that is the point: five methods, no generics, no base
/// class, no expression-tree plumbing. A hand-written repository for one
/// aggregate is a boring file. A generic Repository&lt;T&gt; that tried to serve
/// every aggregate in every service would not be.
/// </summary>
public sealed class OrderRepository : IOrderRepository
{
    private readonly OrderingDbContext _dbContext;

    public OrderRepository(OrderingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Order?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        // TRACKED - the caller may be about to cancel it. The owned Address
        // comes along automatically because it lives in the same row; only the
        // items need an Include.
        return _dbContext.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetByCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .Where(order => order.CustomerId == customerId)
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<Order> Orders, int TotalCount)> GetPageAsync(
        int page,
        int pageSize,
        OrderStatus? status,
        CancellationToken cancellationToken)
    {
        IQueryable<Order> query = _dbContext.Orders.AsNoTracking();

        if (status is not null)
        {
            query = query.Where(order => order.Status == status);
        }

        // The COUNT runs against the filter but without the Include - counting
        // rows does not need the items joined in, and asking for them would
        // make SQLite do work whose result is thrown away.
        var totalCount = await query.CountAsync(cancellationToken);

        var orders = await query
            .Include(order => order.Items)
            // CreatedAtUtc alone is not unique enough to page on: two orders
            // placed in the same tick could swap between page 1 and page 2.
            // The id makes the ordering total.
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (orders, totalCount);
    }

    public void Add(Order order)
    {
        // Staging only. Nothing reaches SQLite until SaveChangesAsync, which
        // keeps "build the aggregate" and "commit it" as two visible steps.
        _dbContext.Orders.Add(order);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
