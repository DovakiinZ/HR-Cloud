using HR.Application.Reports.Registry;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Reports;

public sealed class ReportObjectIdResolver : IReportObjectIdResolver
{
    private readonly Dictionary<string, Guid> _map;

    public ReportObjectIdResolver(ApplicationDbContext db)
        => _map = db.ObjectDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Select(o => new { o.Code, o.Id }).ToList()
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

    public Guid? ResolveId(string objectCode)
        => _map.TryGetValue(objectCode, out var id) ? id : null;
}
