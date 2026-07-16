using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HR.Application.Engines.Finance.Export;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>A rendered export payload: the file bytes plus the HTTP metadata a controller needs to stream it.</summary>
public sealed record ReportExportFile(byte[] Content, string ContentType, string FileName);

public interface IReportExportService
{
    /// <param name="parameters">Must match the on-screen run's parameter values, or the exported
    /// file silently disagrees with what the user was looking at.</param>
    Task<ReportExportFile> ExportAsync(Guid reportId, ExportFormat format, IReadOnlyDictionary<string, string?>? parameters, CancellationToken ct);
}
