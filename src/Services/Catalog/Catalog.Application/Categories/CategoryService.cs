using Catalog.Application.Abstractions;
using Catalog.Application.Categories.Dtos;
using Catalog.Application.Exceptions;
using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Catalog.Application.Categories;

public sealed class CategoryService : ICategoryService
{
    private readonly ICatalogDbContext _dbContext;
    private readonly ILogger<CategoryService> _logger;

    public CategoryService(ICatalogDbContext dbContext, ILogger<CategoryService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CategoryResponse>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        // AsNoTracking: this is a read-only path. Without it, EF Core would take
        // a snapshot of every returned entity so it can detect changes later -
        // pure waste when nothing will be modified or saved.
        var categories = await _dbContext.Categories
            .AsNoTracking()
            .OrderBy(category => category.Name)
            .ToListAsync(cancellationToken);

        return categories
            .Select(CategoryMappings.ToResponse)
            .ToList();
    }

    public async Task<CategoryResponse?> GetByIdAsync(
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        var category = await _dbContext.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == categoryId, cancellationToken);

        // The nullable return type is the contract: the compiler forces the
        // controller to decide what "not found" means in HTTP terms.
        return category is null ? null : CategoryMappings.ToResponse(category);
    }

    public async Task<CategoryResponse> CreateAsync(
        CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        // Normalise with the entity's own rule so we search for exactly the
        // value that would be stored - otherwise "Keyboards" and "keyboards"
        // would both pass this check and then collide in the unique index.
        var slug = Category.NormalizeSlug(request.Slug);

        var slugTaken = await _dbContext.Categories
            .AnyAsync(candidate => candidate.Slug == slug, cancellationToken);

        if (slugTaken)
        {
            throw new DuplicateCategorySlugException(slug);
        }

        var category = Category.Create(request.Name, request.Slug);

        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created category {CategoryId} with slug {Slug}",
            category.Id,
            category.Slug);

        return CategoryMappings.ToResponse(category);
    }

    public async Task<CategoryResponse?> UpdateAsync(
        Guid categoryId,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        // No AsNoTracking here, and that is the whole point: we are about to
        // change this entity, so EF Core must track it to work out the UPDATE.
        var category = await _dbContext.Categories
            .FirstOrDefaultAsync(candidate => candidate.Id == categoryId, cancellationToken);

        if (category is null)
        {
            return null;
        }

        var slug = Category.NormalizeSlug(request.Slug);

        // "Some OTHER category already owns this slug" - excluding ourselves,
        // otherwise saving a category without changing its slug would conflict
        // with itself.
        var slugTaken = await _dbContext.Categories
            .AnyAsync(
                candidate => candidate.Slug == slug && candidate.Id != categoryId,
                cancellationToken);

        if (slugTaken)
        {
            throw new DuplicateCategorySlugException(slug);
        }

        category.Update(request.Name, request.Slug);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated category {CategoryId}", category.Id);

        return CategoryMappings.ToResponse(category);
    }

    public async Task<bool> DeactivateAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        var category = await _dbContext.Categories
            .FirstOrDefaultAsync(candidate => candidate.Id == categoryId, cancellationToken);

        if (category is null)
        {
            return false;
        }

        // A business rule, not an input rule: we need the current state of other
        // rows to decide, which is why it cannot live in a validation attribute.
        var activeProductCount = await _dbContext.Products
            .CountAsync(
                product => product.CategoryId == categoryId && product.IsActive,
                cancellationToken);

        if (activeProductCount > 0)
        {
            throw new CategoryHasActiveProductsException(categoryId, activeProductCount);
        }

        category.Deactivate();

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deactivated category {CategoryId}", category.Id);

        return true;
    }
}
