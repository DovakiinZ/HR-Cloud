using HR.Domain.Engines.Reports;

namespace HR.Modules.Platform.Services.Reports;

public interface IReportObjectResolver
{
    /// <param name="parameters">Values for filters marked IsParameter, keyed by field code
    /// (case-insensitive); ":to" suffix supplies a Between upper bound. Null uses stored defaults.</param>
    Task<ReportExecutionModel> BuildModelAsync(
        ReportDefinition report,
        IReadOnlyDictionary<string, string?>? parameters,
        CancellationToken ct);
}
