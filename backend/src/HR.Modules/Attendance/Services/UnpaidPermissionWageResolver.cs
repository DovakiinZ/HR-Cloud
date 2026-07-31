using HR.Domain.Engines.Attendance;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Attendance.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Attendance.Services;

/// <summary>Contract for resolving the wage basis used for unpaid-permission deductions.
/// Reads from the active AttendancePolicy; falls back to (BasicSalary, 30, 8) when no policy exists.</summary>
public interface IUnpaidPermissionWageResolver
{
    /// <summary>
    /// Returns (MonthlyWage, DivisorDays, DailyPayableHours) for an employee on a specific date.
    /// MonthlyWage = BasicSalary + Σ active allowances whose AllowanceType.Code is in the policy CSV.
    /// DivisorDays is derived from the policy's UnpaidDivisorBasis.
    /// </summary>
    Task<(decimal MonthlyWage, int DivisorDays, decimal DailyPayableHours)> ResolveAsync(
        Guid employeeId, DateTime date, CancellationToken ct = default);
}

/// <summary>EF Core / DB-backed implementation of <see cref="IUnpaidPermissionWageResolver"/>.</summary>
public sealed class UnpaidPermissionWageResolver : IUnpaidPermissionWageResolver
{
    private readonly ApplicationDbContext _db;

    public UnpaidPermissionWageResolver(ApplicationDbContext db) => _db = db;

    public async Task<(decimal MonthlyWage, int DivisorDays, decimal DailyPayableHours)> ResolveAsync(
        Guid employeeId, DateTime date, CancellationToken ct = default)
    {
        // Load the active default policy (highest IsDefault priority).
        var policy = await _db.AttendancePolicies.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.IsDefault)
            .FirstOrDefaultAsync(ct);

        // ── Divisor days ─────────────────────────────────────────────────────
        int divisorDays = ComputeDivisorDays(policy, date);

        // ── Daily payable hours ───────────────────────────────────────────────
        decimal dailyPayableHours = policy is not null ? policy.UnpaidDailyPayableHours : 8m;
        if (dailyPayableHours <= 0m) dailyPayableHours = 8m;

        // ── Monthly wage ─────────────────────────────────────────────────────
        var basicSalary = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => e.BasicSalary)
            .FirstOrDefaultAsync(ct);

        decimal monthlyWage = basicSalary;

        // Add configured allowance components.
        var componentCsv = policy?.UnpaidWageComponentCodes;
        if (!string.IsNullOrWhiteSpace(componentCsv))
        {
            var codes = componentCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (codes.Count > 0)
            {
                // Resolve AllowanceType MasterDataItem IDs whose Code is in the CSV.
                var allowanceTypeIds = await _db.MasterDataItems.AsNoTracking()
                    .Where(m => m.ObjectType == HR.Domain.Engines.MasterData.MasterDataObjectType.AllowanceType
                                && codes.Contains(m.Code))
                    .Select(m => m.Id)
                    .ToListAsync(ct);

                if (allowanceTypeIds.Count > 0)
                {
                    var allowanceSum = await _db.EmployeeAllowances.AsNoTracking()
                        .Where(a => a.EmployeeId == employeeId
                                    && a.IsActive
                                    && allowanceTypeIds.Contains(a.AllowanceTypeId))
                        .SumAsync(a => (decimal?)a.Amount, ct) ?? 0m;

                    monthlyWage += allowanceSum;
                }
            }
        }

        return (monthlyWage, divisorDays, dailyPayableHours);
    }

    private static int ComputeDivisorDays(AttendancePolicy? policy, DateTime date)
    {
        if (policy is null) return 30;

        return policy.UnpaidDivisorBasis switch
        {
            DayBasis.Fixed30 => 30,
            DayBasis.CalendarMonth => DateTime.DaysInMonth(date.Year, date.Month),
            DayBasis.WorkingDays => CountWorkingDays(policy, date),
            _ => 30,
        };
    }

    /// <summary>Count non-weekend days in the month of <paramref name="date"/>. Uses the
    /// default weekend (Fri=5, Sat=6) because the policy has no shift context here.
    /// Falls back to 30 if the count resolves to 0.</summary>
    private static int CountWorkingDays(AttendancePolicy policy, DateTime date)
    {
        // Default weekend days (Fri+Sat = 5,6 in DayOfWeek ints).
        var weekendInts = new HashSet<int> { 5, 6 };

        int count = 0;
        int daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
        for (int d = 1; d <= daysInMonth; d++)
        {
            var dow = (int)new DateTime(date.Year, date.Month, d).DayOfWeek;
            if (!weekendInts.Contains(dow)) count++;
        }
        return count > 0 ? count : 30;
    }
}
