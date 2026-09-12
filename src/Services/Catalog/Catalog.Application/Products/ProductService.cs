using Catalog.Application.Abstractions;
using Catalog.Application.Common;
using Catalog.Application.Exceptions;
using Catalog.Application.Products.Dtos;
using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Catalog.Application.Products;

public sealed class ProductService : IProductService
{
    private readonly ICatalogDbContext _dbContext;
    private readonly ILogger<ProductService> _logger;

    public ProductService(ICatalogDbContext dbContext, ILogger<ProductService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PagedResponse<ProductResponse>> GetAsync(
        ProductListQuery query,
        CancellationToken cancellationToken)
    {
        // Belt and braces: DataAnnotations already rejected page < 1, but this
        // method is also callable from code that never went through model
        // binding (tests, future background jobs), so it defends itself.
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, ProductListQuery.MaxPageSize);

        // ---------------------------------------------------------------
        // QUERY COMPOSITION
        //
        // Each `if` below adds a WHERE clause to an expression tree. Nothing
        // has touched SQLite yet: IQueryable is a *description* of a query.
        // The database is only contacted when we call CountAsync / ToListAsync
        // ("deferred execution"). That is what lets us build a different SQL
        // statement per request without writing string concatenation - and
        // without any risk of SQL injection, because every value becomes a
        // parameter.
        // ---------------------------------------------------------------
        IQueryable<Product> products = _dbContext.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";

            // EF.Functions.Like maps to SQL LIKE, which SQLite treats as
            // case-insensitive for ASCII. string.Contains would map to instr(),
            // which is case-sensitive - not what a search box should do.
            products = products.Where(product =>
                EF.Functions.Like(product.Name, pattern) ||
                EF.Functions.Like(product.Sku, pattern));
        }

        if (query.CategoryId is not null)
        {
            var categoryId = query.CategoryId.Value;
            products = products.Where(product => product.CategoryId == categoryId);
        }

        if (query.MinPrice is not null)
        {
            var minPrice = query.MinPrice.Value;
            products = products.Where(product => product.Price >= minPrice);
        }

        if (query.MaxPrice is not null)
        {
            var maxPrice = query.MaxPrice.Value;
            products = products.Where(product => product.Price <= maxPrice);
        }

        if (query.IsActive is not null)
        {
            var isActive = query.IsActive.Value;
            products = products.Where(product => product.IsActive == isActive);
        }

        // First database round trip: SELECT COUNT(*) with the same filters.
        // The client needs the total to render paging, and a page of 20 rows
        // cannot tell it how many there are altogether.
        var totalCount = await products.CountAsync(cancellationToken);

        // Second round trip: the page itself.
        //
        // ThenBy(Id) matters. OFFSET/LIMIT over a non-unique sort order can
        // return the same row on two pages and skip another, because SQLite is
        // free to break ties differently between queries. Adding the primary
        // key makes the order total, and therefore paging stable.
        var rows = await products
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            // Projecting the category name inside the query turns into a JOIN.
            // The `!` is a compiler hint only: inside an expression tree it is
            // never executed, it just tells the nullable analyser to relax.
            .Select(product => new
            {
                Product = product,
                CategoryName = product.Category!.Name
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => ProductMappings.ToResponse(row.Product, row.CategoryName))
            .ToList();

        return new PagedResponse<ProductResponse>(items, page, pageSize, totalCount);
    }

    public async Task<ProductResponse?> GetByIdAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.Id == productId)
            .Select(product => new
            {
                Product = product,
                CategoryName = product.Category!.Name
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : ProductMappings.ToResponse(row.Product, row.CategoryName);
    }

    public async Task<ProductResponse> CreateAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        // The category must exist. There is a real foreign key in the database
        // that would also stop us, but hitting it produces a raw provider error;
        // checking first lets us return a message that says which id was wrong.
        var category = await _dbContext.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == request.CategoryId,
                cancellationToken);

        if (category is null)
        {
            throw new ReferencedCategoryNotFoundException(request.CategoryId);
        }

        var sku = Product.NormalizeSku(request.Sku);

        // NOTE - a deliberate, known gap:
        // Between this check and SaveChangesAsync, another request could insert
        // the same SKU. The unique index on Products.Sku is the real guarantee
        // and would reject the second insert with a constraint violation, which
        // currently surfaces as a 500. Turning that race into a clean 409 is a
        // concurrency problem, and we handle concurrency properly in Phase 6
        // rather than half-solving it here.
        var skuTaken = await _dbContext.Products
            .AnyAsync(product => product.Sku == sku, cancellationToken);

        if (skuTaken)
        {
            throw new DuplicateSkuException(sku);
        }

        var product = Product.Create(
            request.Name,
            request.Description,
            request.Sku,
            request.Price,
            request.CategoryId);

        // Add only tells the change tracker "this is a new row". No SQL yet.
        _dbContext.Products.Add(product);

        // This is where the INSERT is generated and committed.
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created product {ProductId} with SKU {Sku} in category {CategoryId}",
            product.Id,
            product.Sku,
            product.CategoryId);

        return ProductMappings.ToResponse(product, category.Name);
    }

    public async Task<ProductResponse?> UpdateAsync(
        Guid productId,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        // Tracked on purpose - see CategoryService.UpdateAsync.
        var product = await _dbContext.Products
            .FirstOrDefaultAsync(candidate => candidate.Id == productId, cancellationToken);

        if (product is null)
        {
            return null;
        }

        var category = await _dbContext.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == request.CategoryId,
                cancellationToken);

        if (category is null)
        {
            throw new ReferencedCategoryNotFoundException(request.CategoryId);
        }

        var sku = Product.NormalizeSku(request.Sku);

        var skuTaken = await _dbContext.Products
            .AnyAsync(
                candidate => candidate.Sku == sku && candidate.Id != productId,
                cancellationToken);

        if (skuTaken)
        {
            throw new DuplicateSkuException(sku);
        }

        // All the rules about what a valid product looks like live in the
        // entity. This layer decides *whether* to call it; the entity decides
        // what the call is allowed to do.
        product.Update(
            request.Name,
            request.Description,
            request.Sku,
            request.Price,
            request.CategoryId);

        // No Update() call needed: the entity is tracked, so EF Core compares it
        // against the snapshot taken when it was loaded and writes only the
        // columns that actually changed.
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated product {ProductId}", product.Id);

        return ProductMappings.ToResponse(product, category.Name);
    }

    public async Task<bool> DeactivateAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products
            .FirstOrDefaultAsync(candidate => candidate.Id == productId, cancellationToken);

        if (product is null)
        {
            return false;
        }

        product.Deactivate();

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deactivated product {ProductId}", product.Id);

        return true;
    }
}
