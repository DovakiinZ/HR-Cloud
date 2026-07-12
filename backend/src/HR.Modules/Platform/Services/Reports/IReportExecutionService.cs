namespace HR.Modules.Platform.Services.Reports;

public interface IReportExecutionService
{
    Task<ReportResult> RunAsync(Guid reportId, int page, int pageSize, CancellationToken ct);
}
