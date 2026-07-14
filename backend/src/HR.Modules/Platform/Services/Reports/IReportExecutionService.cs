namespace HR.Modules.Platform.Services.Reports;

public interface IReportExecutionService
{
    Task<ReportResult> RunAsync(Guid reportId, int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// Runs the same pipeline as <see cref="RunAsync"/> but returns ALL rows up to <c>RowCap</c>
    /// (no per-page slice). <see cref="ReportResult.Truncated"/> is <c>true</c> when the source
    /// row count exceeded <c>RowCap</c>. Intended for export paths only.
    /// </summary>
    Task<ReportResult> RunForExportAsync(Guid reportId, CancellationToken ct);
}
