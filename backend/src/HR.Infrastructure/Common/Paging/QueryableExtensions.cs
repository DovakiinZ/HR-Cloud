using HR.Application.Common.Paging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace HR.Infrastructure.Common.Paging;

/// <summary>
/// IQueryable extension that materialises a <see cref="PagedResult{T}"/> from a query.
/// Uses EF Core async operators when an EF provider is present; falls back to synchronous
/// LINQ when the query is backed by an ordinary in-memory IQueryable (e.g. in unit tests
/// that use plain Enumerable.AsQueryable()).
/// </summary>
public static class QueryableExtensions
{
    private const int MinPage = 1;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 200;

    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PagedRequest request,
        CancellationToken ct)
    {
        var page     = Math.Max(MinPage, request.Page);
        var pageSize = Math.Clamp(request.PageSize, MinPageSize, MaxPageSize);

        int total;
        List<T> items;

        if (query.Provider is IAsyncQueryProvider)
        {
            // EF Core or another async-capable provider — use async operators.
            total = await query.CountAsync(ct);
            items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);
        }
        else
        {
            // Plain LINQ (e.g. Enumerable.AsQueryable()) — synchronous fallback.
            total = query.Count();
            items = query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }

        return new PagedResult<T>(items, page, pageSize, total);
    }
}
