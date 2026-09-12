namespace Catalog.Application.Common;

/// <summary>
/// The shape every paged list endpoint returns.
///
/// A bare JSON array is a trap: the client cannot tell whether it received
/// everything or only the first slice, and it has no way to build paging
/// controls. Returning the page metadata alongside the items makes the contract
/// self-describing.
/// </summary>
/// <param name="Items">The rows on this page.</param>
/// <param name="Page">1-based page number that was served.</param>
/// <param name="PageSize">Maximum rows per page (may be clamped by the server).</param>
/// <param name="TotalCount">Total rows matching the filter, across all pages.</param>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    /// <summary>
    /// Computed, not stored. System.Text.Json serialises read-only properties,
    /// so this still appears in the JSON response - one less value the caller
    /// has to calculate and get wrong.
    /// </summary>
    public int TotalPages => PageSize <= 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
