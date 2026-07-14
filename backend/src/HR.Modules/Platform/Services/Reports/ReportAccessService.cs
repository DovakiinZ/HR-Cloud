using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HR.Application.Common.Interfaces;
using HR.Application.Common.Exceptions;
using HR.Domain.Engines.Reports;
using HR.Modules.Employees.Entities;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Reports;

public sealed class ReportAccessService : IReportAccessService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _user;

    public ReportAccessService(ApplicationDbContext db, ICurrentUserService user)
    { _db = db; _user = user; }

    public async Task<ReportAccessContext> BuildContextAsync(CancellationToken ct)
    {
        var uid = _user.UserId;
        var roleIds = await _db.UserRoles.Where(ur => ur.UserId == uid)
            .Select(ur => ur.RoleId).ToListAsync(ct);
        var deptId = await _db.Set<Employee>().Where(e => e.UserId == uid)
            .Select(e => e.DepartmentId).FirstOrDefaultAsync(ct);
        return new ReportAccessContext
        {
            UserId = uid,
            DepartmentId = deptId,
            RoleIds = new HashSet<Guid>(roleIds),
        };
    }

    public async Task<IQueryable<ReportDefinition>> FilterVisibleAsync(IQueryable<ReportDefinition> source, CancellationToken ct)
    {
        var ctx = await BuildContextAsync(ct);
        return source.Where(ReportVisibilityPredicate.Build(ctx));
    }

    public async Task EnsureCanReadAsync(Guid reportId, CancellationToken ct)
    {
        var (report, shares, ctx) = await LoadAsync(reportId, ct);
        if (!ReportAccessResolver.CanRead(report, shares, ctx))
            throw new ForbiddenException("You do not have access to this report.");
    }

    public async Task EnsureCanEditAsync(Guid reportId, CancellationToken ct)
    {
        var (report, shares, ctx) = await LoadAsync(reportId, ct);
        if (!ReportAccessResolver.CanEdit(report, shares, ctx))
            throw new ForbiddenException("You do not have permission to edit this report.");
    }

    private async Task<(ReportDefinition, IReadOnlyList<ReportShare>, ReportAccessContext)> LoadAsync(Guid reportId, CancellationToken ct)
    {
        var report = await _db.Set<ReportDefinition>().Include(r => r.Shares)
            .FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new NotFoundException("ReportDefinition", reportId);
        var ctx = await BuildContextAsync(ct);
        return (report, report.Shares.ToList(), ctx);
    }
}
