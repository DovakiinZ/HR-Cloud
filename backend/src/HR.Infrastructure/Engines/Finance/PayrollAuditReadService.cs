using HR.Application.Common.Paging;
using HR.Application.Engines.Finance;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Engines.Finance;

/// <summary>Unified payroll audit read over the existing AuditLog. Every payroll engine already logs its
/// actions (creation, each state transition, void/amend/reissue, transaction actions) via IAuditLogService,
/// so this only filters, orders, pages, and resolves actor names — no new audit system.</summary>
public sealed class PayrollAuditReadService : IPayrollAuditReadService
{
    private readonly ApplicationDbContext _db;
    public PayrollAuditReadService(ApplicationDbContext db) => _db = db;

    public async Task<PagedResult<PayrollAuditRow>> QueryAsync(PayrollAuditFilter f, PagedRequest request, CancellationToken ct = default)
    {
        // All payroll audit rows use a "Payroll*" EntityType (PayrollRun/PayrollTransaction/PayrollPayslip).
        var q = _db.AuditLogs.AsNoTracking().Where(a => a.EntityType.StartsWith("Payroll"));

        if (f.RunId is { } rid) q = q.Where(a => a.EntityId == rid);
        if (f.ActorUserId is { } uid) q = q.Where(a => a.UserId == uid);
        if (!string.IsNullOrWhiteSpace(f.Action)) q = q.Where(a => a.Action.Contains(f.Action!));
        if (f.From is { } from) q = q.Where(a => a.Timestamp >= from);
        if (f.To is { } to) q = q.Where(a => a.Timestamp <= to);

        var total = await q.CountAsync(ct);
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 200);

        var rows = await q.OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * size).Take(size)
            .Select(a => new { a.Timestamp, a.Action, a.UserId, a.EntityType, a.EntityId, a.OldValues, a.NewValues })
            .ToListAsync(ct);

        var actorIds = rows.Where(r => r.UserId != null).Select(r => r.UserId!.Value).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Email })
            .ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName, ct);

        var items = rows.Select(r => new PayrollAuditRow(
            r.Timestamp, r.Action, r.UserId,
            r.UserId is { } id && names.TryGetValue(id, out var n) ? n : null,
            r.EntityType, r.EntityId, r.OldValues, r.NewValues)).ToList();

        return new PagedResult<PayrollAuditRow>(items, page, size, total);
    }
}
