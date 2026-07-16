using System.Threading;
using System.Threading.Tasks;

namespace HR.Modules.Platform.Services.Reports;

public interface IReportScheduleRunner
{
    Task<int> RunDueAsync(CancellationToken ct);
}
