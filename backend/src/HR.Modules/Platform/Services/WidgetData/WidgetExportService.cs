using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Finance.Export;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.WidgetData;

public sealed class WidgetExportService : IWidgetExportService
{
    private readonly IWidgetDataService _data;
    private readonly IEnumerable<IExportWriter> _writers;
    private readonly ApplicationDbContext _db;

    public WidgetExportService(IWidgetDataService data, IEnumerable<IExportWriter> writers, ApplicationDbContext db)
    { _data = data; _writers = writers; _db = db; }

    public async Task<WidgetExportFile> ExportAsync(Guid widgetId, ExportFormat format, CancellationToken ct)
    {
        var writer = _writers.FirstOrDefault(w => w.Format == format)
            ?? throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("format", $"Unsupported export format '{format}'.") });

        var name = await _db.DashboardWidgets.Where(w => w.Id == widgetId).Select(w => w.TitleEn).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("DashboardWidget", widgetId);

        var result = await _data.ExecuteWidgetAsync(widgetId, null, ct);
        var dataset = WidgetResultFlattener.Flatten(result, name);
        var bytes = writer.Write(dataset);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd");
        var safe = string.IsNullOrWhiteSpace(name) ? "widget" : System.Text.RegularExpressions.Regex.Replace(name, "[\\\\/:*?\"<>|]+", "_");
        return new WidgetExportFile(bytes, writer.ContentType, $"{safe}-{stamp}.{writer.Extension}");
    }

    private const int MaxExportRows = 5000;
    private const int PageSize = 200;

    public async Task<WidgetExportFile> ExportRowsAsync(WidgetQuerySpec spec, string? segmentKey,
        IReadOnlyList<WidgetFilterSpec>? dashboardFilters, ExportFormat format, string title, CancellationToken ct)
    {
        var writer = _writers.FirstOrDefault(w => w.Format == format)
            ?? throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("format", $"Unsupported export format '{format}'.") });

        var allRows = new List<Dictionary<string, object?>>();
        List<TableColumn>? columns = null;
        long totalCount = 0;
        int page = 1;

        while (true)
        {
            var result = await _data.GetRowsAsync(spec, segmentKey, dashboardFilters, page, PageSize, ct);

            if (page == 1)
            {
                columns = result.Columns;
                totalCount = result.TotalCount;
            }

            if (result.Rows.Count == 0)
                break;

            allRows.AddRange(result.Rows);

            if (allRows.Count >= totalCount || allRows.Count >= MaxExportRows)
                break;

            page++;
        }

        if (allRows.Count > MaxExportRows)
            allRows.RemoveRange(MaxExportRows, allRows.Count - MaxExportRows);

        var combined = new WidgetDataResult
        {
            Kind = "table",
            Columns = columns ?? new List<TableColumn>(),
            Rows = allRows,
            TotalCount = allRows.Count,
        };

        var name = string.IsNullOrWhiteSpace(title) ? "details" : title;
        var dataset = WidgetResultFlattener.Flatten(combined, name);
        var bytes = writer.Write(dataset);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd");
        var safe = System.Text.RegularExpressions.Regex.Replace(name, "[\\\\/:*?\"<>|]+", "_");
        return new WidgetExportFile(bytes, writer.ContentType, $"{safe}-{stamp}.{writer.Extension}");
    }
}
