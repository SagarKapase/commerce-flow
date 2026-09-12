using System.ComponentModel.DataAnnotations;

namespace Catalog.Application.Products.Dtos;

/// <summary>
/// The query-string parameters of GET /api/products.
///
/// Binding these into one object instead of seven separate action parameters
/// keeps the controller signature readable, gives Swagger a documented shape,
/// and lets DataAnnotations validate paging in the usual way.
///
/// Every filter is nullable on purpose: null means "do not filter by this",
/// which is different from "filter by false" or "filter by zero".
/// </summary>
public sealed class ProductListQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Free-text search over product name and SKU. Case-insensitive.</summary>
    public string? Search { get; init; }

    public Guid? CategoryId { get; init; }

    /// <summary>Inclusive lower bound on price, in major units (e.g. 10.00).</summary>
    public decimal? MinPrice { get; init; }

    /// <summary>Inclusive upper bound on price, in major units (e.g. 99.99).</summary>
    public decimal? MaxPrice { get; init; }

    /// <summary>null returns both active and inactive products.</summary>
    public bool? IsActive { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Page must be 1 or greater.")]
    public int Page { get; init; } = 1;

    /// <summary>
    /// Upper-bounded at 100. Without a cap, one caller sending pageSize=1000000
    /// pulls the whole table into memory - the single most common cause of an
    /// API falling over in production.
    /// </summary>
    [Range(1, MaxPageSize, ErrorMessage = "PageSize must be between 1 and 100.")]
    public int PageSize { get; init; } = DefaultPageSize;
}
