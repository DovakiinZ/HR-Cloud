using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HR.Domain.Engines.Reports;

namespace HR.Modules.Platform.Services.Reports;

public interface IReportAccessService
{
    Task<ReportAccessContext> BuildContextAsync(CancellationToken ct);
    Task<IQueryable<ReportDefinition>> FilterVisibleAsync(IQueryable<ReportDefinition> source, CancellationToken ct);
    Task EnsureCanReadAsync(System.Guid reportId, CancellationToken ct);
    Task EnsureCanEditAsync(System.Guid reportId, CancellationToken ct);
}
