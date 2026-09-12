namespace Ordering.Application.Common;

/// <summary>
/// The shape every paged list endpoint in this service returns.
///
/// Identical to Catalog's PagedResponse, and duplicated on purpose. It is a
/// wire contract: if Ordering and Catalog shared one compiled type, adding a
/// "hasNextPage" field for one of them would force a rebuild and redeploy of
/// the other. Fourteen lines is a cheaper price than that coupling.
///
/// The line to hold: cross-cutting MECHANISM can be shared (token validation);
/// contracts that cross a service boundary cannot.
/// </summary>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
