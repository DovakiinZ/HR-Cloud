using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance;
using HR.Domain.Engines.Finance.StateMachine;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Engines.Finance;

/// <summary>Create-time guard: resolves whether an immutable payroll run already covers the period
/// that <paramref name="effectiveDate"/> maps to for the given employee. If so, throws
/// <see cref="PayrollPeriodClosedException"/> so the caller is prevented from mutating a frozen period.
///
/// Algorithm:
/// 1. Load all population rows for the employee that carry IsIncluded=true, joined to their run and
///    the version that was pinned at run-creation (to obtain CutoffDay + CarryToNextPeriod).
/// 2. For each row whose run is immutable, resolve the period of effectiveDate using
///    <see cref="PayrollPeriodResolver"/> with that version's cutoff settings.
/// 3. If the resolved (year, month) matches the run's TargetPeriodYear/TargetPeriodMonth, the period
///    is closed — throw.
/// </summary>
public sealed class PayrollPeriodGuard : IPayrollPeriodGuard
{
    private readonly ApplicationDbContext _db;

    public PayrollPeriodGuard(ApplicationDbContext db) => _db = db;

    public async Task EnsurePeriodOpenForAsync(
        Guid employeeId, DateTime effectiveDate, CancellationToken ct = default)
    {
        // Pull every population row for this employee (included) with the associated run + version.
        var candidates = await (
            from pop in _db.PayrollRunPopulations
            where pop.EmployeeId == employeeId && pop.IsIncluded
            join run in _db.PayrollRuns on pop.PayrollRunId equals run.Id
            join ver in _db.PayrollDefinitionVersions on run.PayrollDefinitionVersionId equals ver.Id
            select new
            {
                RunId              = run.Id,
                run.RunNumber,
                run.State,
                run.PayrollDefinitionId,
                run.TargetPeriodYear,
                run.TargetPeriodMonth,
                ver.CutoffDay,
                ver.CarryToNextPeriod,
            })
            .ToListAsync(ct);

        foreach (var c in candidates)
        {
            // Skip mutable runs — they can still accept new transactions.
            if (!PayrollRunStateMachine.IsImmutable(c.State)) continue;

            // Resolve which (year, month) the effective date belongs to under this version's cutoff.
            var (year, month) = PayrollPeriodResolver.Resolve(effectiveDate, c.CutoffDay, c.CarryToNextPeriod);

            if (year == c.TargetPeriodYear && month == c.TargetPeriodMonth)
            {
                throw new PayrollPeriodClosedException(new PayrollPeriodClosedPayload(
                    ErrorCode:           "PAYROLL_PERIOD_CLOSED",
                    BlockingRunId:       c.RunId,
                    BlockingRunNumber:   c.RunNumber,
                    PayrollDefinitionId: c.PayrollDefinitionId,
                    TargetPeriodYear:    c.TargetPeriodYear,
                    TargetPeriodMonth:   c.TargetPeriodMonth,
                    BlockingRunState:    c.State.ToString()));
            }
        }
    }
}
