using Basket.Api.Catalog;
using Basket.Api.Domain;
using Basket.Api.Dtos;
using Basket.Api.Exceptions;
using Basket.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basket.Api.Baskets;

public sealed class BasketService : IBasketService
{
    private readonly BasketDbContext _dbContext;
    private readonly ICatalogClient _catalogClient;
    private readonly ILogger<BasketService> _logger;

    public BasketService(
        BasketDbContext dbContext,
        ICatalogClient catalogClient,
        ILogger<BasketService> logger)
    {
        _dbContext = dbContext;
        _catalogClient = catalogClient;
        _logger = logger;
    }

    public async Task<BasketResponse> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        // ------------------------------------------------------------------
        // NOTICE WHAT THIS METHOD DOES NOT DO: call Catalog.
        //
        // It would be easy to re-fetch every product here so the basket always
        // showed today's price. It would also mean a customer cannot look at
        // their own basket while Catalog is restarting, and it would turn one
        // page view into N remote calls.
        //
        // So we read the snapshot taken at add-time. The cost is that a price
        // change is not reflected until the item is re-added; the benefit is
        // that this endpoint has no external dependency at all and keeps
        // working when the rest of the system does not. Prove it in the debug
        // checklist: stop Catalog and read your basket.
        //
        // The price that actually matters is re-checked when the order is
        // placed (Phase 8). A basket is a wish list, not a contract.
        // ------------------------------------------------------------------
        //
        // AsNoTracking: a read. Include: the aggregate is only meaningful with
        // its lines, and without Include the Items collection comes back empty
        // and the totals silently read zero. Lazy loading is not enabled in
        // this project, deliberately - an invisible query per property access
        // is how you get N+1 problems you cannot see in the code.
        var basket = await _dbContext.Baskets
            .AsNoTracking()
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);

        return basket is null
            ? BasketMappings.EmptyFor(userId)
            : BasketMappings.ToResponse(basket);
    }

    public async Task<BasketResponse> AddItemAsync(
        Guid userId,
        AddBasketItemRequest request,
        CancellationToken cancellationToken)
    {
        // --------------------------------------------------------------
        // ASK CATALOG FIRST, BEFORE TOUCHING OUR OWN DATABASE.
        //
        // Order matters. If this call fails, we have created no basket row,
        // opened no transaction and written nothing to undo. Doing the local
        // work first and the remote call second would leave a basket row behind
        // for every request that was always going to fail.
        //
        // Rule of thumb: do the thing most likely to fail first, while failing
        // is still free.
        // --------------------------------------------------------------
        var product = await _catalogClient.GetProductAsync(request.ProductId, cancellationToken);

        if (product is null)
        {
            throw new ProductNotAvailableException(request.ProductId, "it does not exist");
        }

        if (!product.IsActive)
        {
            // Catalog soft-deletes rather than deleting, so a discontinued
            // product still answers a GET. It just cannot be bought - which is
            // a rule only Catalog knows and only Basket can enforce here.
            throw new ProductNotAvailableException(request.ProductId, "it is no longer for sale");
        }

        // Tracked - we are about to change it.
        var basket = await LoadForUpdateAsync(userId, cancellationToken);

        if (basket is null)
        {
            // A basket row is created lazily, on the first add. Reading an
            // empty basket writes nothing, so a user who browses and never buys
            // leaves no rows behind.
            basket = CustomerBasket.Create(userId);
            _dbContext.Baskets.Add(basket);
        }

        // The name and price come from the service that OWNS them. The client
        // supplied neither - it could not have, they are no longer in the DTO.
        //
        // What we store is still a SNAPSHOT. If Catalog changes the price a
        // second after this line runs, the basket keeps the old one until the
        // item is added again. That is deliberate: see GetAsync for why we do
        // not re-fetch on every read, and Phase 8 for where the price is
        // re-checked for real.
        basket.AddItem(
            product.Id,
            product.Name,
            product.Price,
            request.Quantity);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Added product {ProductId} x{Quantity} at {UnitPrice} to basket for user {UserId}",
            product.Id,
            request.Quantity,
            product.Price,
            userId);

        return BasketMappings.ToResponse(basket);
    }

    public async Task<BasketResponse?> UpdateItemQuantityAsync(
        Guid userId,
        Guid productId,
        int quantity,
        CancellationToken cancellationToken)
    {
        var basket = await LoadForUpdateAsync(userId, cancellationToken);

        if (basket is null)
        {
            return null;
        }

        // The aggregate answers whether the line existed. The service does not
        // reach into basket.Items to check - it asks the root, which is the
        // only thing allowed to know how items are stored.
        if (!basket.UpdateItemQuantity(productId, quantity))
        {
            return null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return BasketMappings.ToResponse(basket);
    }

    public async Task<bool> RemoveItemAsync(
        Guid userId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var basket = await LoadForUpdateAsync(userId, cancellationToken);

        if (basket is null || !basket.RemoveItem(productId))
        {
            return false;
        }

        // Removing the item from the aggregate's list is enough. EF compares
        // the collection against the snapshot taken at load time, sees the line
        // is gone, and emits the DELETE. We never call Remove on a DbSet.
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Removed product {ProductId} from basket for user {UserId}",
            productId,
            userId);

        return true;
    }

    public async Task ClearAsync(Guid userId, CancellationToken cancellationToken)
    {
        var basket = await LoadForUpdateAsync(userId, cancellationToken);

        if (basket is null)
        {
            // Nothing to clear, and creating an empty basket just to empty it
            // would be silly. The end state the caller asked for already holds.
            return;
        }

        basket.Clear();

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Cleared basket for user {UserId}", userId);
    }

    /// <summary>
    /// Loads the aggregate with its items, TRACKED, ready to be modified.
    /// One method so no call site can forget the Include and then wonder why
    /// adding an existing product created a duplicate instead of incrementing.
    /// </summary>
    private Task<CustomerBasket?> LoadForUpdateAsync(Guid userId, CancellationToken cancellationToken)
    {
        return _dbContext.Baskets
            .Include(basket => basket.Items)
            .FirstOrDefaultAsync(basket => basket.UserId == userId, cancellationToken);
    }
}
