using HR.Domain.Engines.Reports;

namespace HR.Modules.Platform.Services.Reports;

public interface IReportObjectResolver
{
    Task<ReportExecutionModel> BuildModelAsync(ReportDefinition report, CancellationToken ct);
}
