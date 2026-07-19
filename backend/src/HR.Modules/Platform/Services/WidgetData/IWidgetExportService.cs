using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HR.Application.Engines.Finance.Export;

namespace HR.Modules.Platform.Services.WidgetData;

public sealed record WidgetExportFile(byte[] Content, string ContentType, string FileName);

public interface IWidgetExportService
{
    Task<WidgetExportFile> ExportAsync(Guid widgetId, ExportFormat format, CancellationToken ct);

    Task<WidgetExportFile> ExportRowsAsync(WidgetQuerySpec spec, string? segmentKey,
        IReadOnlyList<WidgetFilterSpec>? dashboardFilters, ExportFormat format, string title, CancellationToken ct);
}
