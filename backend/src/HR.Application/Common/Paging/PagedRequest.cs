namespace HR.Application.Common.Paging;

/// <summary>
/// Shared query contract for all paginated payroll read APIs.
/// Page is 1-based. PageSize defaults to 25 and is clamped to 1–200.
/// Sort, Search, and Filter are opaque strings interpreted by each endpoint.
/// </summary>
public record PagedRequest(
    int Page = 1,
    int PageSize = 25,
    string? Sort = null,
    string? Search = null,
    string? Filter = null);
