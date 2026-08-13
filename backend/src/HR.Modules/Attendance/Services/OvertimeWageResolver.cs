using HR.Infrastructure.Engines.Finance;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Attendance.Services;

/// <summary>Resolves the wage basis for overtime pay: hourly wage (BasicSalary / 30 / 8) and the
/// tenant overtime multiplier (CalcSettingsJson.attendanceRates.overtimeMultiplier, default 1.5 =
/// KSA Labor Law Art. 107) read from the latest published payroll definition version.</summary>
public interface IOvertimeWageResolver
{
    Task<(decimal HourlyWage, decimal OvertimeMultiplier)> ResolveAsync(
        Guid employeeId, CancellationToken ct = default);
}

/// <summary>EF Core / DB-backed implementation of <see cref="IOvertimeWageResolver"/>.</summary>
public sealed class OvertimeWageResolver : IOvertimeWageResolver
{
    private readonly ApplicationDbContext _db;

    public OvertimeWageResolver(ApplicationDbContext db) => _db = db;

    public async Task<(decimal HourlyWage, decimal OvertimeMultiplier)> ResolveAsync(
        Guid employeeId, CancellationToken ct = default)
    {
        var basic = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => e.BasicSalary)
            .FirstOrDefaultAsync(ct);

        var hourly = basic / 30m / 8m;

        var calcJson = await _db.PayrollDefinitionVersions.AsNoTracking()
            .Where(v => v.PublishedAt != null)
            .OrderByDescending(v => v.PublishedAt)
            .Select(v => v.CalcSettingsJson)
            .FirstOrDefaultAsync(ct);

        var multiplier = PayrollCalcSettings.Rates(calcJson).Overtime;

        return (hourly, multiplier);
    }
}
