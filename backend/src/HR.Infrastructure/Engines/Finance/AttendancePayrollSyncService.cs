using HR.Application.Common.Exceptions;
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.MasterData;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Engines.Finance;

public sealed class AttendancePayrollSyncService : IAttendancePayrollSyncService
{
    private const string Source = "Attendance";
    private const string RefType = "AttendancePeriodPenalty";

    private static readonly (AttendancePayrollKind Kind, string Code, string ObjectType, PayrollTransactionKind TxnKind)[] KindSpecs =
    {
        (AttendancePayrollKind.Absence,  "ABSENCE",  MasterDataObjectType.DeductionType, PayrollTransactionKind.Deduction),
        (AttendancePayrollKind.Late,     "LATE",     MasterDataObjectType.DeductionType, PayrollTransactionKind.Deduction),
        (AttendancePayrollKind.Shortage, "SHORTAGE", MasterDataObjectType.DeductionType, PayrollTransactionKind.Deduction),
        (AttendancePayrollKind.Overtime, "OVERTIME", MasterDataObjectType.AdditionType,  PayrollTransactionKind.Addition),
    };

    private readonly ApplicationDbContext _db;
    private readonly IPayrollFactProvider _facts;
    private readonly AttendanceWageCalculator _attendance;
    private readonly IPayrollPeriodGuard _guard;

    public AttendancePayrollSyncService(
        ApplicationDbContext db,
        IPayrollFactProvider facts,
        AttendanceWageCalculator attendance,
        IPayrollPeriodGuard guard)
    { _db = db; _facts = facts; _attendance = attendance; _guard = guard; }

    public async Task<AttendancePayrollSyncReport> SyncAsync(
        PayrollDefinitionVersion version, PayrollPeriod period,
        IReadOnlyCollection<Guid> employeeIds, bool? includeOvertime = null, CancellationToken ct = default)
    {
        var includeDed = PayrollCalcSettings.IncludeAttendanceDeductions(version.CalcSettingsJson);
        var includeOt = includeOvertime ?? PayrollCalcSettings.IncludeOvertime(version.CalcSettingsJson);
        if (employeeIds.Count == 0 || (!includeDed && !includeOt))
            return new AttendancePayrollSyncReport(0, 0, 0, 0, 0);

        var rates = PayrollCalcSettings.Rates(version.CalcSettingsJson);

        // Resolve the master-data ids by code (now includes AdditionType for overtime).
        var typeByKind = await ResolveTypesAsync(includeDed, includeOt, ct); // returns (Guid TypeId, PayrollTransactionKind TxnKind)

        // Wages + aggregate penalty inputs, straight from the fact provider (identical to the retired rule).
        var inputs = await _facts.BuildInputsAsync(version, period, employeeIds, ct);
        var factsByEmp = inputs.ToDictionary(i => i.EmployeeId, i => i.Facts);

        // Per-day drill-down rows for the reference snapshot.
        var rowsByEmp = (await _attendance.BreakdownRowsAsync(employeeIds, period, ct))
            .GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        // Existing attendance-sourced records for this period (any live status).
        var existing = await _db.PayrollTransactions
            .Where(t => t.SourceModule == Source && employeeIds.Contains(t.EmployeeId)
                        && t.TargetPeriodYear == period.Year && t.TargetPeriodMonth == period.Month)
            .ToListAsync(ct);
        var existingByKey = existing
            .GroupBy(t => (t.EmployeeId, t.TypeId))
            .ToDictionary(g => g.Key, g => g.First());

        int created = 0, updated = 0, removed = 0, skipped = 0, processed = 0;

        foreach (var empId in employeeIds)
        {
            if (!factsByEmp.TryGetValue(empId, out var f)) continue;
            var dailyWage = Dec(f, "DailyWage");
            var hourlyWage = Dec(f, "HourlyWage");

            foreach (var spec in KindSpecs)
            {
                var kind = spec.Kind;
                var enabled = kind == AttendancePayrollKind.Overtime ? includeOt : includeDed;
                if (!enabled) continue;
                processed++;

                var amount = kind switch
                {
                    AttendancePayrollKind.Absence  => Math.Round(Dec(f, "AbsentDays")   * dailyWage  * rates.Absence, 2),
                    AttendancePayrollKind.Late     => Math.Round(Dec(f, "LateHours")     * hourlyWage * rates.Late, 2),
                    AttendancePayrollKind.Shortage => Math.Round(Dec(f, "ShortageHours") * hourlyWage * rates.Shortage, 2),
                    AttendancePayrollKind.Overtime => Math.Round(Dec(f, "OvertimeHours") * hourlyWage * rates.Overtime, 2),
                    _ => 0m,
                };
                var (typeId, txnKind) = typeByKind[kind];
                existingByKey.TryGetValue((empId, typeId), out var txn);

                if (txn is { Status: PayrollTransactionStatus.Posted } or { Status: PayrollTransactionStatus.Reversed })
                { skipped++; continue; }

                if (amount <= 0m)
                {
                    if (txn is not null && txn.Status != PayrollTransactionStatus.Cancelled)
                    {
                        txn.Status = PayrollTransactionStatus.Cancelled;
                        txn.StatusReason = "Attendance impact cleared on re-sync.";
                        await ClearRefsAsync(txn.Id, ct);
                        removed++;
                    }
                    continue;
                }

                if (txn is null)
                {
                    // Guard: no path may create a born-Approved transaction against a frozen period.
                    await _guard.EnsurePeriodOpenForAsync(empId, period.Start, ct);

                    txn = new PayrollTransaction
                    {
                        Kind = txnKind,
                        EmployeeId = empId,
                        TypeId = typeId,
                        Amount = amount,
                        EffectiveDate = period.Start,
                        TransactionDate = period.End,
                        TargetPeriodYear = period.Year,
                        TargetPeriodMonth = period.Month,
                        SourceModule = Source,
                        ReferenceType = RefType,
                        Status = PayrollTransactionStatus.Approved,
                    };
                    _db.PayrollTransactions.Add(txn);
                    await _db.SaveChangesAsync(ct);
                    created++;
                }
                else
                {
                    txn.Amount = amount;
                    if (txn.Status == PayrollTransactionStatus.Cancelled)
                    {
                        // Guard: re-activating a Cancelled record also moves it to Approved.
                        await _guard.EnsurePeriodOpenForAsync(empId, txn.EffectiveDate, ct);
                        txn.Status = PayrollTransactionStatus.Approved;
                    }
                    updated++;
                }

                var rateForKind = kind switch
                {
                    AttendancePayrollKind.Absence => rates.Absence, AttendancePayrollKind.Late => rates.Late,
                    AttendancePayrollKind.Shortage => rates.Shortage, _ => rates.Overtime,
                };
                await WriteRefsAsync(txn.Id, kind, dailyWage, hourlyWage, rateForKind,
                    rowsByEmp.TryGetValue(empId, out var rs) ? rs : new(), ct);
            }
        }

        await _db.SaveChangesAsync(ct);
        return new AttendancePayrollSyncReport(created, updated, removed, skipped, processed);
    }

    private async Task<IReadOnlyDictionary<AttendancePayrollKind, (Guid TypeId, PayrollTransactionKind TxnKind)>> ResolveTypesAsync(
        bool includeDed, bool includeOt, CancellationToken ct)
    {
        var found = await _db.MasterDataItems.AsNoTracking()
            .Where(m => (m.ObjectType == MasterDataObjectType.DeductionType || m.ObjectType == MasterDataObjectType.AdditionType))
            .Select(m => new { m.ObjectType, m.Code, m.Id }).ToListAsync(ct);
        var byKey = found.ToDictionary(x => (x.ObjectType, x.Code.ToUpperInvariant()), x => x.Id);
        var map = new Dictionary<AttendancePayrollKind, (Guid, PayrollTransactionKind)>();
        foreach (var spec in KindSpecs)
        {
            // Only require master-data for the kinds that are actually enabled.
            var kindEnabled = spec.Kind == AttendancePayrollKind.Overtime ? includeOt : includeDed;
            if (!kindEnabled) continue;
            if (!byKey.TryGetValue((spec.ObjectType, spec.Code), out var id))
                throw new DomainException($"No {spec.ObjectType} configured for attendance kind '{spec.Kind}' (code {spec.Code}). Re-run payroll bootstrap.");
            map[spec.Kind] = (id, spec.TxnKind);
        }
        return map;
    }

    private async Task WriteRefsAsync(Guid txnId, AttendancePayrollKind kind, decimal dailyWage, decimal hourlyWage,
        decimal multiplier, List<AttendanceBreakdownRow> rows, CancellationToken ct)
    {
        await ClearRefsAsync(txnId, ct);
        foreach (var r in rows.Where(r => r.PenaltyKind == kind))
        {
            var contribution = kind == AttendancePayrollKind.Absence
                ? dailyWage * r.Days * multiplier
                : Math.Round(r.Minutes / 60m, 2) * hourlyWage * multiplier;
            _db.PayrollTransactionAttendanceReferences.Add(new PayrollTransactionAttendanceReference
            {
                PayrollTransactionId = txnId, AttendanceRecordId = r.AttendanceRecordId,
                Date = r.Date, PenaltyKind = kind, Minutes = r.Minutes, Days = r.Days,
                AmountContribution = contribution,
            });
        }
    }

    private async Task ClearRefsAsync(Guid txnId, CancellationToken ct)
    {
        var old = await _db.PayrollTransactionAttendanceReferences
            .Where(r => r.PayrollTransactionId == txnId).ToListAsync(ct);
        if (old.Count > 0) _db.PayrollTransactionAttendanceReferences.RemoveRange(old);
    }

    private static decimal Dec(IReadOnlyDictionary<string, object?> facts, string key) =>
        facts.TryGetValue(key, out var v) && v is not null ? Convert.ToDecimal(v) : 0m;
}
