namespace HR.Application.Common.Paging;

/// <summary>
/// Immutable paged result envelope returned by all paginated payroll read APIs.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The items on the current page.</param>
/// <param name="Page">The 1-based current page number (after clamping).</param>
/// <param name="PageSize">The effective page size (after clamping to 1–200).</param>
/// <param name="Total">Total number of items across all pages.</param>
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int Total);
