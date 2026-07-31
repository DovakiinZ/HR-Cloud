using HR.Application.Engines.Attendance;
using HR.Application.Engines.Scope;
using HR.Domain.Engines.Attendance;
using HR.Domain.Engines.MasterData;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Attendance.Services;

// ── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>The 5 nullable per-type limits (mirrors PermissionTypeRules props; consumed by Task 3 too).</summary>
public sealed record PermissionLimitsDto(
    int? MaxMinutesPerRequest,
    int? MaxMinutesPerDay,
    int? MaxMinutesPerMonth,
    int? MaxRequestsPerDay,
    int? MaxRequestsPerMonth);

/// <summary>Aggregated usage for a single employee + type combination.
/// remaining fields are null when no limit is set at the type level (Task 3 will add policy-fallback).</summary>
public sealed record PermissionUsageDto(
    int UsedMinutesDay,
    int? RemainingMinutesDay,
    int UsedMinutesMonth,
    int? RemainingMinutesMonth,
    int UsedRequestsDay,
    int? RemainingRequestsDay,
    int UsedRequestsMonth,
    int? RemainingRequestsMonth);

/// <summary>One eligible permission type with its limits and the caller's usage counters.</summary>
public sealed record EligiblePermissionTypeDto(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    bool Paid,
    HR.Domain.Engines.Attendance.PermissionExceedBehavior ExceedBehavior,
    PermissionUsageDto Usage,
    PermissionLimitsDto Limits);

/// <summary>Thin context record passed to the executor / cap evaluator.</summary>
public sealed record PermissionTypeContext(MasterDataItem Item, PermissionTypeRules Rules);

// ── Interface ─────────────────────────────────────────────────────────────────

public interface IAttendancePermissionTypeService
{
    /// <summary>Returns every active AttendancePermissionType the given employee is eligible for,
    /// together with their current-day and current-month usage counts.</summary>
    Task<IReadOnlyList<EligiblePermissionTypeDto>> GetEligibleTypesAsync(Guid employeeId, CancellationToken ct);

    /// <summary>Resolves a single type by code (or id string) for the given employee.
    /// Returns null if the type doesn't exist or the employee is not eligible.</summary>
    Task<PermissionTypeContext?> ResolveForRequestAsync(Guid employeeId, string typeCodeOrId, CancellationToken ct);

    /// <summary>Counts the employee's already-approved permissions for the given type
    /// on <paramref name="date"/> (today) and for the whole calendar month containing <paramref name="date"/>.</summary>
    Task<HR.Domain.Engines.Attendance.PermissionUsageTally> TallyAsync(
        Guid employeeId, Guid typeId, DateTime date, CancellationToken ct);
}

// ── Implementation ────────────────────────────────────────────────────────────

/// <summary>Resolves which attendance permission types an employee is eligible for, based on the
/// type-level SelectionScope stored in MetadataJson.Eligibility, and computes usage counters.</summary>
public sealed class AttendancePermissionTypeService : IAttendancePermissionTypeService
{
    private readonly ApplicationDbContext _db;
    private readonly IScopeEngine _scope;

    public AttendancePermissionTypeService(ApplicationDbContext db, IScopeEngine scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<IReadOnlyList<EligiblePermissionTypeDto>> GetEligibleTypesAsync(
        Guid employeeId, CancellationToken ct)
    {
        var activeTypes = await _db.MasterDataItems.AsNoTracking()
            .Where(m => m.ObjectType == MasterDataObjectType.AttendancePermissionType && m.IsActive)
            .ToListAsync(ct);

        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Load ALL permissions for this employee this month once; ComputeUsage filters per-type.
        var permissionsThisMonth = await _db.AttendancePermissions.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId && p.Date >= monthStart)
            .ToListAsync(ct);

        var result = new List<EligiblePermissionTypeDto>();

        foreach (var item in activeTypes)
        {
            var rules = PermissionTypeRules.Parse(item.MetadataJson);

            if (!await IsEligibleAsync(employeeId, rules, ct)) continue;

            // Pass the type's Id so ComputeUsage only tallies rows stamped with this type.
            var usage = ComputeUsage(permissionsThisMonth, today, rules, item.Id);

            result.Add(new EligiblePermissionTypeDto(
                item.Id,
                item.Code,
                item.NameAr,
                item.NameEn,
                rules.Paid,
                rules.ExceedBehavior,
                usage,
                new PermissionLimitsDto(
                    rules.MaxMinutesPerRequest,
                    rules.MaxMinutesPerDay,
                    rules.MaxMinutesPerMonth,
                    rules.MaxRequestsPerDay,
                    rules.MaxRequestsPerMonth)));
        }

        return result;
    }

    public async Task<PermissionTypeContext?> ResolveForRequestAsync(
        Guid employeeId, string typeCodeOrId, CancellationToken ct)
    {
        // Try by Code first; if it looks like a Guid, also try by Id.
        MasterDataItem? item = await _db.MasterDataItems.AsNoTracking()
            .Where(m => m.ObjectType == MasterDataObjectType.AttendancePermissionType
                        && m.IsActive
                        && m.Code == typeCodeOrId)
            .FirstOrDefaultAsync(ct);

        if (item is null && Guid.TryParse(typeCodeOrId, out var typeId))
        {
            item = await _db.MasterDataItems.AsNoTracking()
                .Where(m => m.ObjectType == MasterDataObjectType.AttendancePermissionType
                            && m.IsActive
                            && m.Id == typeId)
                .FirstOrDefaultAsync(ct);
        }

        if (item is null) return null;

        var rules = PermissionTypeRules.Parse(item.MetadataJson);

        // Finding 4: also return null when the employee is not eligible for this type.
        if (!await IsEligibleAsync(employeeId, rules, ct)) return null;

        return new PermissionTypeContext(item, rules);
    }

    public async Task<HR.Domain.Engines.Attendance.PermissionUsageTally> TallyAsync(
        Guid employeeId, Guid typeId, DateTime date, CancellationToken ct)
    {
        var dayStart = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        var monthStart = new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1);

        var monthPerms = await _db.AttendancePermissions.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId
                     && p.PermissionTypeId == typeId
                     && p.Date >= monthStart
                     && p.Date < monthEnd)
            .Select(p => new { p.Date, p.ExcusedMinutes })
            .ToListAsync(ct);

        var dayPerms = monthPerms.Where(p => p.Date.Date == dayStart.Date).ToList();

        return new HR.Domain.Engines.Attendance.PermissionUsageTally(
            UsedMinutesDay:    dayPerms.Sum(p => p.ExcusedMinutes),
            UsedMinutesMonth:  monthPerms.Sum(p => p.ExcusedMinutes),
            UsedRequestsDay:   dayPerms.Count,
            UsedRequestsMonth: monthPerms.Count);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>True when the employee is eligible for the type based on its SelectionScope eligibility rule.
    /// null / Mode="All" ⇒ eligible for everyone; otherwise calls the scope engine.</summary>
    private async Task<bool> IsEligibleAsync(Guid employeeId, PermissionTypeRules rules, CancellationToken ct)
    {
        var eligibility = rules.Eligibility;

        // No eligibility filter = entire company.
        if (eligibility is null) return true;

        // Mode All = entire company (scope engine would return everyone; skip call for efficiency).
        if (string.Equals(eligibility.Mode, "All", StringComparison.OrdinalIgnoreCase)) return true;

        // Criteria mode: ask the scope engine and check if the employee is in the resolved set.
        var resolution = await _scope.ResolveAsync(eligibility, ct);
        return resolution.IncludedEmployeeIds.Contains(employeeId);
    }

    /// <summary>Counts today's and this-month's usage for the employee from already-loaded permissions,
    /// filtered to only rows attributed to <paramref name="typeId"/> (null-typed rows never match).</summary>
    private static PermissionUsageDto ComputeUsage(
        List<AttendancePermission> permissionsThisMonth,
        DateTime today,
        PermissionTypeRules rules,
        Guid typeId)
    {
        // Only rows stamped with this specific type count toward its limits (Finding 1 fix).
        // Rows with PermissionTypeId == null (legacy or pre-executor rows) are excluded from
        // all per-type tallies; they match no specific type.
        var typePermsMonth = permissionsThisMonth.Where(p => p.PermissionTypeId == typeId).ToList();
        var todayPerms = typePermsMonth.Where(p => p.Date.Date == today).ToList();

        int usedMinutesDay = todayPerms.Sum(p => p.ExcusedMinutes);
        int usedRequestsDay = todayPerms.Count;
        int usedMinutesMonth = typePermsMonth.Sum(p => p.ExcusedMinutes);
        int usedRequestsMonth = typePermsMonth.Count;

        // remaining = limit - used when the type-level limit is set; else null.
        // Task 3 will add policy-level fallback resolution.
        int? remainingMinutesDay = rules.MaxMinutesPerDay.HasValue
            ? Math.Max(0, rules.MaxMinutesPerDay.Value - usedMinutesDay)
            : null;
        int? remainingMinutesMonth = rules.MaxMinutesPerMonth.HasValue
            ? Math.Max(0, rules.MaxMinutesPerMonth.Value - usedMinutesMonth)
            : null;
        int? remainingRequestsDay = rules.MaxRequestsPerDay.HasValue
            ? Math.Max(0, rules.MaxRequestsPerDay.Value - usedRequestsDay)
            : null;
        int? remainingRequestsMonth = rules.MaxRequestsPerMonth.HasValue
            ? Math.Max(0, rules.MaxRequestsPerMonth.Value - usedRequestsMonth)
            : null;

        return new PermissionUsageDto(
            usedMinutesDay,
            remainingMinutesDay,
            usedMinutesMonth,
            remainingMinutesMonth,
            usedRequestsDay,
            remainingRequestsDay,
            usedRequestsMonth,
            remainingRequestsMonth);
    }
}
