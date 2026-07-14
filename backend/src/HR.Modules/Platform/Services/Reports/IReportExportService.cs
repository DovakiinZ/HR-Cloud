using System;
using System.Threading;
using System.Threading.Tasks;
using HR.Application.Engines.Finance.Export;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>A rendered export payload: the file bytes plus the HTTP metadata a controller needs to stream it.</summary>
public sealed record ReportExportFile(byte[] Content, string ContentType, string FileName);

public interface IReportExportService
{
    Task<ReportExportFile> ExportAsync(Guid reportId, ExportFormat format, CancellationToken ct);
}
