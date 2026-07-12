using System.Data;
using System.Data.Common;
using HR.Application.Common.Exceptions;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>
/// Orchestrates the full report execution pipeline:
///   load definition → resolve object model → build SQL → execute via ADO → shape rows → return result.
/// ADO connection lifecycle mirrors <see cref="HR.Modules.Platform.Services.WidgetData.WidgetDataService"/>.
/// </summary>
public sealed class ReportExecutionService : IReportExecutionService
{
    private const int RowCap = 5000;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IReportObjectResolver _resolver;

    public ReportExecutionService(
        ApplicationDbContext db,
        ICurrentUserService user,
        IReportObjectResolver resolver)
    {
        _db = db;
        _user = user;
        _resolver = resolver;
    }

    public async Task<ReportResult> RunAsync(Guid reportId, int page, int pageSize, CancellationToken ct)
    {
        // Clamp paging parameters.
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        // 1. Load the report definition with all child collections.
        var report = await _db.Set<ReportDefinition>().AsNoTracking()
            .Include(r => r.Fields)
            .Include(r => r.Filters)
            .Include(r => r.Groupings)
            .Include(r => r.Sortings)
            .Include(r => r.Relationships)
            .FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new NotFoundException("ReportDefinition", reportId);

        // 2. Resolve into an execution model (objects, joins, columns, filters, sorts, computed).
        var model = await _resolver.BuildModelAsync(report, ct);

        // 3. Build parameterized SQL (LIMIT RowCap+1 so we can detect truncation).
        var (sql, parameters) = ReportSqlBuilder.Build(model.Query, _user.TenantId, RowCap);

        // 4. Execute via raw ADO, keying each row by OutputCode.
        var rows = new List<ReportRow>();
        await ReadAsync(sql, parameters, ct, reader =>
        {
            var row = new ReportRow();
            foreach (var col in model.Query.Columns)
            {
                var ord = reader.GetOrdinal(col.OutputCode);
                row[col.OutputCode] = reader.IsDBNull(ord) ? null : reader.GetValue(ord);
            }
            rows.Add(row);
        });

        // 5. Detect truncation and trim to RowCap.
        var truncated = rows.Count > RowCap;
        if (truncated) rows = rows.Take(RowCap).ToList();

        // 6. Shape (computed columns, in-memory sorts, grouping, paging, aggregates).
        var shaper = new ReportRowShaper(new ComputedFieldEvaluator());
        return shaper.Shape(rows, new ReportShapeSpec
        {
            ReportCode = report.Code,
            Columns = model.OutputColumns,
            Computed = model.Computed,
            GroupByCodes = model.GroupByCodes,
            InMemorySorts = model.InMemorySorts,
            Page = page,
            PageSize = pageSize,
            Truncated = truncated,
        });
    }

    // ── ADO helpers (mirrors WidgetDataService pattern) ───────────────────────

    /// <summary>
    /// Executes <paramref name="sql"/> with positional parameters (named p0, p1, …) and invokes
    /// <paramref name="onRow"/> for each row in the result set.
    /// Opens the connection only if it is not already open and closes it when done.
    /// </summary>
    private async Task ReadAsync(
        string sql,
        IReadOnlyList<object?> parameters,
        CancellationToken ct,
        Action<DbDataReader> onRow)
    {
        var conn = _db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        for (int i = 0; i < parameters.Count; i++)
        {
            var param = cmd.CreateParameter();
            param.ParameterName = "p" + i;
            param.Value = parameters[i] ?? DBNull.Value;
            cmd.Parameters.Add(param);
        }

        var (_, opened) = await OpenAsync(conn, ct);
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) onRow(reader);
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
    }

    private static async Task<(DbConnection conn, bool opened)> OpenAsync(DbConnection conn, CancellationToken ct)
    {
        if (conn.State == ConnectionState.Open) return (conn, false);
        await conn.OpenAsync(ct);
        return (conn, true);
    }
}
