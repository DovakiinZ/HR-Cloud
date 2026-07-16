namespace HR.Modules.Platform.Services.Reports;

public interface IReportExecutionService
{
    /// <param name="parameters">Values for filters marked IsParameter, keyed by field code
    /// (case-insensitive); ":to" suffix supplies a Between upper bound. Null uses stored defaults.</param>
    Task<ReportResult> RunAsync(
        Guid reportId, int page, int pageSize,
        IReadOnlyDictionary<string, string?>? parameters, CancellationToken ct);

    /// <summary>
    /// Runs the same pipeline as <see cref="RunAsync"/> but returns ALL rows up to <c>RowCap</c>
    /// (no per-page slice). <see cref="ReportResult.Truncated"/> is <c>true</c> when the source
    /// row count exceeded <c>RowCap</c>. Intended for export paths only.
    /// </summary>
    /// <param name="parameters">Must match the values used for the on-screen run, or the export
    /// silently disagrees with what the user is looking at.</param>
    Task<ReportResult> RunForExportAsync(
        Guid reportId,
        IReadOnlyDictionary<string, string?>? parameters, CancellationToken ct);
}
