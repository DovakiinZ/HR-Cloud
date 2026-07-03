using HR.Application.Common.Exceptions;
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.Finance.StateMachine;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Engines.Finance;

/// <summary>Creates a PayrollTransaction from the run-page quick-action panel (design decision D10).
/// The transaction inherits the run's definition/period/employee, has its EffectiveDate defaulted to
/// the run's PeriodStart (day 1 always resolves to the run's own period regardless of cutoff/carry),
/// and is created PendingApproval with Origin=RunPage + CreatedFromRunId stamped.</summary>
public sealed class CreateFromRunService : ICreateFromRunService
{
    private readonly ApplicationDbContext _db;
    private readonly IPayrollTransactionService _txns;

    public CreateFromRunService(ApplicationDbContext db, IPayrollTransactionService txns)
    {
        _db   = db;
        _txns = txns;
    }

    public async Task<Guid> CreateAsync(Guid runId, CreateFromRunRequest req, CancellationToken ct)
    {
        // Load run; guard immutability.
        var run = await _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new InvalidOperationException($"Run {runId} not found.");

        if (PayrollRunStateMachine.IsImmutable(run.State))
            throw new DomainException(
                "PAYROLL_RUN_IMMUTABLE: cannot add transactions to a closed run.");

        // Load the pinned definition version for cutoff/carry settings.
        var ver = await _db.PayrollDefinitionVersions
                      .FirstOrDefaultAsync(v => v.Id == run.PayrollDefinitionVersionId, ct)
                  ?? throw new DomainException(
                      "PAYROLL_DEFINITION_VERSION_MISSING: the run's pinned definition version was not found.");

        // EffectiveDate: default to run.PeriodStart (UTC).
        // Day 1 of the target month is always ≤ CutoffDay, so it always resolves to the run's OWN period
        // regardless of CutoffDay or CarryToNextPeriod — avoiding a false PAYROLL_EFFECTIVE_DATE_OUT_OF_PERIOD
        // error that the old PeriodEnd default produced for carry=true runs whose PeriodEnd is month-end.
        // If a date is supplied by the caller it must still resolve to the run's period.
        var effective = req.EffectiveDate.HasValue
            ? req.EffectiveDate.Value
            : DateTime.SpecifyKind(run.PeriodStart, DateTimeKind.Utc);

        var (ry, rm) = PayrollPeriodResolver.Resolve(effective, ver.CutoffDay, ver.CarryToNextPeriod);
        if (ry != run.TargetPeriodYear || rm != run.TargetPeriodMonth)
            throw new DomainException(
                "PAYROLL_EFFECTIVE_DATE_OUT_OF_PERIOD: EffectiveDate must fall in the run's period.");

        return await _txns.CreateAsync(new CreatePayrollTransactionArgs(
            req.Kind,
            req.EmployeeId,
            req.TypeId,
            req.Amount,
            effective,
            TransactionDate:    null,
            IsRecurring:        false,
            RecurrenceEndDate:  null,
            Notes:              req.Notes,
            AttachmentFileId:   null,
            SubmitImmediately:  true,
            Origin:             PayrollTransactionOrigin.RunPage,
            CreatedFromRunId:   runId), ct);
    }
}
