using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Notifications;

/// <summary>Reuses the resolution queries formerly private to RequestEngine, returning ALL matching
/// users (so "HR team" means everyone in the HR role, not the first). Empty result = unresolved;
/// the dispatcher logs and skips. Never substitutes a different recipient.</summary>
public sealed class NotificationRecipientResolver : INotificationRecipientResolver
{
    private readonly ApplicationDbContext _db;
    public NotificationRecipientResolver(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<Guid>> ResolveAsync(
        RecipientSpec spec, RequestInstance instance, RequestApproval? currentStep, CancellationToken ct)
    {
        switch (spec.Type)
        {
            case NotificationRecipientType.Requester:
            case NotificationRecipientType.EmployeeConcerned:
                return await UsersForEmployeeAsync(instance.EmployeeId, ct);

            case NotificationRecipientType.DirectManager:
            {
                var mgrId = await _db.Employees.Where(e => e.Id == instance.EmployeeId)
                    .Select(e => e.ManagerId).FirstOrDefaultAsync(ct);
                return mgrId is { } m ? await UsersForEmployeeAsync(m, ct) : Array.Empty<Guid>();
            }

            case NotificationRecipientType.DepartmentManager:
            {
                var deptId = await _db.Employees.Where(e => e.Id == instance.EmployeeId)
                    .Select(e => e.DepartmentId).FirstOrDefaultAsync(ct);
                if (deptId is not { } d) return Array.Empty<Guid>();
                var headEmpId = await _db.Departments.Where(x => x.Id == d)
                    .Select(x => x.ManagerId).FirstOrDefaultAsync(ct);
                return headEmpId is { } h ? await UsersForEmployeeAsync(h, ct) : Array.Empty<Guid>();
            }

            case NotificationRecipientType.SpecificEmployee:
                return spec.RefId is { } empId ? await UsersForEmployeeAsync(empId, ct) : Array.Empty<Guid>();

            case NotificationRecipientType.Role:
                return spec.RefId is { } roleId ? await UsersInRoleIdAsync(roleId, ct) : Array.Empty<Guid>();

            case NotificationRecipientType.HrTeam:
                return await UsersInRoleKeywordAsync("HR", ct);

            case NotificationRecipientType.FinanceTeam:
                return await UsersInRoleKeywordAsync("Finance", ct);

            case NotificationRecipientType.CurrentApprover:
            {
                var uid = currentStep?.AssignedToUserId
                    ?? await _db.RequestApprovals
                        .Where(a => a.RequestInstanceId == instance.Id && a.Status == RequestApprovalStatus.Pending)
                        .OrderBy(a => a.StepOrder).Select(a => a.AssignedToUserId).FirstOrDefaultAsync(ct);
                return uid is { } u ? new[] { u } : Array.Empty<Guid>();
            }

            case NotificationRecipientType.PreviousApprover:
            {
                var uid = await _db.RequestApprovals
                    .Where(a => a.RequestInstanceId == instance.Id && a.DecidedByUserId != null)
                    .OrderByDescending(a => a.StepOrder).Select(a => a.DecidedByUserId).FirstOrDefaultAsync(ct);
                return uid is { } u ? new[] { u } : Array.Empty<Guid>();
            }

            case NotificationRecipientType.StepAssignees:
                return await _db.RequestApprovals
                    .Where(a => a.RequestInstanceId == instance.Id && a.AssignedToUserId != null)
                    .Select(a => a.AssignedToUserId!.Value).Distinct().ToListAsync(ct);

            default:
                return Array.Empty<Guid>(); // deferred/greenfield types: caller logs + skips
        }
    }

    private async Task<IReadOnlyList<Guid>> UsersForEmployeeAsync(Guid employeeId, CancellationToken ct)
    {
        var uid = await _db.Employees.Where(e => e.Id == employeeId).Select(e => e.UserId).FirstOrDefaultAsync(ct);
        return uid is { } u ? new[] { u } : Array.Empty<Guid>();
    }

    private async Task<IReadOnlyList<Guid>> UsersInRoleIdAsync(Guid roleId, CancellationToken ct)
    {
        return await (from ur in _db.UserRoles
                      join usr in _db.Users on ur.UserId equals usr.Id
                      where ur.RoleId == roleId && usr.IsActive
                      select usr.Id).Distinct().ToListAsync(ct);
    }

    private async Task<IReadOnlyList<Guid>> UsersInRoleKeywordAsync(string keyword, CancellationToken ct)
    {
        return await (from u in _db.Users.Where(u => u.IsActive)
                      join ur in _db.UserRoles on u.Id equals ur.UserId
                      join role in _db.Roles on ur.RoleId equals role.Id
                      where EF.Functions.ILike(role.Name, $"%{keyword}%")
                      select u.Id).Distinct().ToListAsync(ct);
    }
}
