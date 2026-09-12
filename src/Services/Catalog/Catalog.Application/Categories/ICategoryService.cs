using Catalog.Application.Categories.Dtos;

namespace Catalog.Application.Categories;

/// <summary>
/// Category use cases.
///
/// The interface exists so the controller depends on a contract rather than a
/// concrete class - which is what lets DI substitute a fake in a test and what
/// keeps the controller honest about how little it knows.
///
/// Note the return types. "Not found" is expressed as a null result or a false
/// flag, NOT an exception: a caller asking for a category that does not exist
/// is an ordinary outcome, and exceptions are for the extraordinary.
/// </summary>
public interface ICategoryService
{
    Task<IReadOnlyList<CategoryResponse>> GetAllAsync(CancellationToken cancellationToken);

    /// <returns>The category, or <c>null</c> when no category has that id.</returns>
    Task<CategoryResponse?> GetByIdAsync(Guid categoryId, CancellationToken cancellationToken);

    Task<CategoryResponse> CreateAsync(
        CreateCategoryRequest request,
        CancellationToken cancellationToken);

    /// <returns>The updated category, or <c>null</c> when no category has that id.</returns>
    Task<CategoryResponse?> UpdateAsync(
        Guid categoryId,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken);

    /// <returns><c>true</c> if the category exists and is now inactive; <c>false</c> if it was not found.</returns>
    Task<bool> DeactivateAsync(Guid categoryId, CancellationToken cancellationToken);
}
