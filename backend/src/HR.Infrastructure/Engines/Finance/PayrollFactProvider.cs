using HR.Application.Engines.Finance;
using HR.Application.Engines.Scope;
using HR.Domain.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HR.Infrastructure.Engines.Finance;

/// <summary>Resolves the employee population for a payroll version and builds each employee's fact bag.
/// Mirrors the canonical salary breakdown (basic + capped allowances + additions − deductions − GOSI,
/// with allowance caps and GOSI-inclusion read from master-data metadata) but emits the raw inputs as
/// facts so the rule set — not this provider — decides how they combine.</summary>
public sealed class PayrollFactProvider : IPayrollFactProvider
{
    private readonly ApplicationDbContext _db;
    private readonly IScopeEngine _scope;
    private readonly AttendanceWageCalculator _attendance;

    public PayrollFactProvider(ApplicationDbContext db, IScopeEngine scope, AttendanceWageCalculator attendance)
    { _db = db; _scope = scope; _attendance = attendance; }

    /// <summary>Daily wage for the period under the configured proration basis.</summary>
    public static decimal DailyWageFor(DayBasis basis, decimal monthlyWage, int year, int month, int workingDays)
    {
        var divisor = basis switch
        {
            DayBasis.Fixed30 => 30m,
            DayBasis.CalendarMonth => DateTime.DaysInMonth(year, month),
            DayBasis.WorkingDays => workingDays > 0 ? workingDays : 30m,
            _ => 30m,
        };
        return Math.Round(monthlyWage / divisor, 4);
    }

    public async Task<IReadOnlyList<EmployeePayrollInput>> BuildInputsAsync(
        PayrollDefinitionVersion version, PayrollPeriod period,
        IReadOnlyCollection<Guid>? restrictToEmployeeIds = null, CancellationToken ct = default)
    {
        HashSet<Guid> empIdSet;
        if (restrictToEmployeeIds is { Count: > 0 })
        {
            empIdSet = restrictToEmployeeIds.ToHashSet();          // run: use the frozen population
        }
        else
        {
            var resolution = await _scope.ResolveAsync(
                SelectionScopeJson.Parse(version.SelectionScopeJson), ct);
            empIdSet = resolution.IncludedEmployeeIds.ToHashSet();  // preview: live resolution
        }
        if (empIdSet.Count == 0) return Array.Empty<EmployeePayrollInput>();

        var employees = await _db.Employees.AsNoTracking().Where(e => empIdSet.Contains(e.Id)).ToListAsync(ct);
        if (employees.Count == 0) return Array.Empty<EmployeePayrollInput>();

        var empIds = employees.Select(e => e.Id).ToList();

        var allowances = await _db.EmployeeAllowances.AsNoTracking()
            .Where(a => empIds.Contains(a.EmployeeId) && a.IsActive)
            .Select(a => new { a.EmployeeId, a.AllowanceTypeId, a.Amount }).ToListAsync(ct);
        var additions = await _db.EmployeeAdditions.AsNoTracking()
            .Where(a => empIds.Contains(a.EmployeeId) && a.IsActive)
            .Select(a => new { a.EmployeeId, a.Amount }).ToListAsync(ct);
        var deductions = await _db.EmployeeDeductions.AsNoTracking()
            .Where(a => empIds.Contains(a.EmployeeId) && a.IsActive)
            .Select(a => new { a.EmployeeId, a.Amount }).ToListAsync(ct);

        var gosiRate = await _db.CompanyProfiles.Select(c => (decimal?)c.GosiRate).FirstOrDefaultAsync(ct) ?? 9.75m;

        // Allowance-type metadata (cap + GOSI inclusion).
        var allowTypeIds = allowances.Select(a => a.AllowanceTypeId).Distinct().ToList();
        var allowMeta = new Dictionary<Guid, (decimal? Max, bool Gosi)>();
        var md = await _db.MasterDataItems.AsNoTracking()
            .Where(m => allowTypeIds.Contains(m.Id))
            .Select(m => new { m.Id, m.MetadataJson }).ToListAsync(ct);
        foreach (var m in md) allowMeta[m.Id] = ParseAllowanceRules(m.MetadataJson);

        // Department names (for categorical rules like Department == "Sales").
        var depIds = employees.Where(e => e.DepartmentId.HasValue).Select(e => e.DepartmentId!.Value).ToHashSet();
        var deps = await _db.Departments.AsNoTracking().Where(d => depIds.Contains(d.Id))
            .Select(d => new { d.Id, Name = d.NameAr ?? d.Name }).ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        // Attendance aggregates for the period (shared with the attendance-deduction sync so records and
        // the inert facts can never drift). Shortage on Absent days is excluded inside the calculator.
        var attendance = (await _attendance.AggregateAsync(empIds, period, ct))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var allowByEmp = allowances.GroupBy(a => a.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var addByEmp = additions.GroupBy(a => a.EmployeeId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var dedByEmp = deductions.GroupBy(a => a.EmployeeId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        // Loan installments DUE in this period (unpaid, active loan) → a LOAN deduction line on the payslip.
        // Period-bound by DueMonth so a run never double-deducts the same installment across periods.
        var loanByEmp = (await _db.LoanInstallments.AsNoTracking()
            .Join(_db.Loans.AsNoTracking(), i => i.LoanId, l => l.Id,
                (i, l) => new { l.EmployeeId, i.DueMonth, i.Amount, i.Paid, l.Status })
            .Where(x => empIds.Contains(x.EmployeeId) && !x.Paid && x.Status == "Active"
                        && x.DueMonth >= period.Start && x.DueMonth <= period.End)
            .GroupBy(x => x.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct))
            .ToDictionary(x => x.EmployeeId, x => x.Total);

        // Approved expenses explicitly flagged for payroll whose chosen month (PayrollMonth, 1st of month)
        // falls in this period → an EXPENSE reimbursement (earning) line on the payslip. Period-bound by
        // PayrollMonth so an expense is paid exactly once, in the month the user selected.
        var expenseByEmp = (await _db.Expenses.AsNoTracking()
            .Where(e => empIds.Contains(e.EmployeeId) && e.Status == "Approved"
                        && e.IncludeInPayroll && e.PayrollMonth != null
                        && e.PayrollMonth >= period.Start && e.PayrollMonth <= period.End)
            .GroupBy(e => e.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Total = g.Sum(e => e.Amount) })
            .ToListAsync(ct))
            .ToDictionary(x => x.EmployeeId, x => x.Total);

        var inputs = new List<EmployeePayrollInput>(employees.Count);
        foreach (var e in employees)
        {
            decimal totalAllowances = 0m, gosiAllowanceBase = 0m;
            if (allowByEmp.TryGetValue(e.Id, out var al))
            {
                foreach (var a in al)
                {
                    var amount = a.Amount;
                    var rule = allowMeta.TryGetValue(a.AllowanceTypeId, out var r) ? r : (Max: (decimal?)null, Gosi: false);
                    if (rule.Max is { } max && max > 0 && amount > max) amount = max;
                    totalAllowances += amount;
                    if (rule.Gosi) gosiAllowanceBase += amount;
                }
            }

            var totalAdditions = addByEmp.TryGetValue(e.Id, out var ad) ? ad : 0m;
            var totalDeductions = dedByEmp.TryGetValue(e.Id, out var de) ? de : 0m;
            attendance.TryGetValue(e.Id, out var att);
            var hasAttendance = att is not null && att.Days > 0;

            // Wage-rate basis for attendance deductions: full monthly wage prorated by DayBasis, / 8 hours.
            var monthlyWage = e.BasicSalary + totalAllowances;
            var dailyWage = DailyWageFor(version.DayBasis, monthlyWage, period.Start.Year, period.Start.Month,
                att?.Days ?? 0);
            var hourlyWage = Math.Round(dailyWage / 8m, 4);
            var absentDays = att?.AbsentDays ?? 0;
            var lateHours = att is null ? 0m : Math.Round(att.LateMinutes / 60m, 2);
            var shortageHours = att is null ? 0m : Math.Round(att.ShortageMinutes / 60m, 2);

            var facts = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["BasicSalary"] = e.BasicSalary,
                ["TotalAllowances"] = totalAllowances,
                ["GosiAllowanceBase"] = gosiAllowanceBase,
                ["GosiBase"] = e.BasicSalary + gosiAllowanceBase,
                ["TotalAdditions"] = totalAdditions,
                ["TotalDeductions"] = totalDeductions,
                ["LoanInstallment"] = loanByEmp.TryGetValue(e.Id, out var loanDue) ? loanDue : 0m,
                ["ExpenseReimbursement"] = expenseByEmp.TryGetValue(e.Id, out var expDue) ? expDue : 0m,
                // Effective GOSI rate: employee override → tenant default, or 0 when disabled for the employee.
                ["GosiRate"] = HR.Domain.Engines.Finance.GosiCalculation.EffectiveRate(e.GosiEnabled, e.GosiRateOverride, gosiRate),
                ["WorkedDays"] = att?.Days ?? 0,
                ["OvertimeHours"] = att is null ? 0m : Math.Round(att.OvertimeMinutes / 60m, 2),
                // Attendance-driven deduction inputs (auto-computed from the attendance engine).
                ["DailyWage"] = dailyWage,
                ["HourlyWage"] = hourlyWage,
                ["AbsentDays"] = absentDays,
                ["LateHours"] = lateHours,
                ["ShortageHours"] = shortageHours,
                ["Department"] = e.DepartmentId.HasValue && deps.TryGetValue(e.DepartmentId.Value, out var dn) ? dn : "",
                ["Currency"] = string.IsNullOrWhiteSpace(e.Currency) ? version.Currency : e.Currency,
            };

            inputs.Add(new EmployeePayrollInput
            {
                EmployeeId = e.Id,
                EmployeeNumber = e.EmployeeNumber,
                EmployeeName = $"{e.FirstNameAr ?? e.FirstName} {e.LastNameAr ?? e.LastName}".Trim(),
                Currency = (string.IsNullOrWhiteSpace(e.Currency) ? version.Currency : e.Currency!).ToUpperInvariant(),
                Facts = facts,
                HasAttendanceData = hasAttendance,
            });
        }

        return inputs;
    }

    private static (decimal? Max, bool Gosi) ParseAllowanceRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, false);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return (null, false);
            decimal? max = null; bool gosi = false;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.NameEquals("maxAmount") && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDecimal(out var mx))
                    max = mx;
                else if (p.NameEquals("gosiApplicable") && (p.Value.ValueKind == JsonValueKind.True || p.Value.ValueKind == JsonValueKind.False))
                    gosi = p.Value.GetBoolean();
            }
            return (max, gosi);
        }
        catch (JsonException) { return (null, false); }
    }
}
