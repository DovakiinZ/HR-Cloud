using System.Diagnostics;
using System.Text.Json;
using HR.Application.Common.Exceptions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance;
using HR.Application.Engines.Scope;
using HR.Domain.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.Finance.StateMachine;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Engines.Finance;

/// <summary>Drives a payroll run through its lifecycle. Calculate freezes immutable payslip snapshots;
/// Validate gates progress and freezes the validation report; Approve locks the figures. Every state
/// change goes through the <see cref="PayrollRunStateMachine"/> and is recorded as a transition + audit
/// entry. Execution/ledger-posting is added by the batch orchestrator in the next pass.</summary>
public sealed class PayrollRunEngine : IPayrollRunEngine
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly ApplicationDbContext _db;
    private readonly PayrollComputation _computation;
    private readonly IPayrollValidationEngine _validation;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogService _audit;
    private readonly IScopeEngine _scope;
    private readonly IAttendancePayrollSyncService _attendanceSync;
    private readonly IPayrollRunStalenessEvaluator _staleness;

    public PayrollRunEngine(
        ApplicationDbContext db,
        PayrollComputation computation,
        IPayrollValidationEngine validation,
        ICurrentUserService currentUser,
        IAuditLogService audit,
        IScopeEngine scope,
        IAttendancePayrollSyncService attendanceSync,
        IPayrollRunStalenessEvaluator staleness)
    {
        _db = db;
        _computation = computation;
        _validation = validation;
        _currentUser = currentUser;
        _audit = audit;
        _scope = scope;
        _attendanceSync = attendanceSync;
        _staleness = staleness;
    }

    private Guid? Actor => _currentUser.IsAuthenticated ? _currentUser.UserId : null;

    public async Task<PayrollRun> CreateAsync(Guid payrollDefinitionId, PayrollPeriod period, CancellationToken ct = default)
    {
        var definition = await _db.PayrollDefinitions.FirstOrDefaultAsync(d => d.Id == payrollDefinitionId, ct)
            ?? throw new InvalidOperationException($"Payroll definition {payrollDefinitionId} not found.");
        if (definition.CurrentVersionId is not { } versionId)
            throw new InvalidOperationException("Payroll definition has no published version to run.");

        var version = await _db.PayrollDefinitionVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct)
            ?? throw new InvalidOperationException("Published payroll definition version not found.");

        var run = new PayrollRun
        {
            RunNumber = await NextRunNumberAsync(ct),
            PayrollDefinitionId = definition.Id,
            PayrollDefinitionVersionId = version.Id,
            RuleSetVersionId = version.RuleSetVersionId,
            PeriodStart = period.Start,
            PeriodEnd = period.End,
            // Immutable period identity — stamped once at creation, never reassigned.
            TargetPeriodYear = period.Year,
            TargetPeriodMonth = period.Month,
            State = PayrollRunState.Draft,
            Currency = version.Currency,
        };
        _db.PayrollRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        // Freeze the resolved population so future org changes never alter this run.
        var resolution = await _scope.ResolveAsync(
            SelectionScopeJson.Parse(version.SelectionScopeJson), ct);
        var included = resolution.IncludedEmployeeIds.ToHashSet();
        var snapshotEmployees = await _db.Employees.AsNoTracking()
            .Where(e => included.Contains(e.Id))
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.FirstNameAr, e.LastName, e.LastNameAr,
                               e.DepartmentId, e.BranchId, e.JobTitleId, e.PaymentMethodId })
            .ToListAsync(ct);
        foreach (var e in snapshotEmployees)
            _db.PayrollRunPopulations.Add(new PayrollRunPopulation
            {
                PayrollRunId = run.Id, EmployeeId = e.Id,
                EmployeeNumber = e.EmployeeNumber,
                EmployeeName = $"{e.FirstNameAr ?? e.FirstName} {e.LastNameAr ?? e.LastName}".Trim(),
                DepartmentId = e.DepartmentId, BranchId = e.BranchId, JobTitleId = e.JobTitleId,
                PaymentMethodId = e.PaymentMethodId, IsIncluded = true,
            });
        foreach (var ex in resolution.ExcludedByScope)
            _db.PayrollRunPopulations.Add(new PayrollRunPopulation
            {
                PayrollRunId = run.Id, EmployeeId = ex.EmployeeId,
                IsIncluded = false, ExclusionReasonCode = "ExcludedByScope",
            });
        run.EmployeeCount = included.Count;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("PayrollRunCreated", nameof(PayrollRun), run.Id,
            null, new { run.RunNumber, run.PayrollDefinitionId, run.PeriodStart, run.PeriodEnd }, ct);
        return run;
    }

    public async Task<PayrollRun> CalculateAsync(Guid runId, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var run = await LoadRunAsync(runId, ct);
        if (run.State is not (PayrollRunState.Draft or PayrollRunState.Preview
                              or PayrollRunState.Validated or PayrollRunState.PendingApproval))
            throw new InvalidOperationException($"A run can only be calculated while Draft, Preview, Validated, or PendingApproval (was {run.State}).");

        var version = await _db.PayrollDefinitionVersions.FirstOrDefaultAsync(v => v.Id == run.PayrollDefinitionVersionId, ct)
            ?? throw new InvalidOperationException("Payroll definition version not found.");
        var period = new PayrollPeriod(run.PeriodStart, run.PeriodEnd);

        // Load the frozen included employee ids — historical runs are never affected by org changes.
        var frozen = await _db.PayrollRunPopulations.AsNoTracking()
            .Where(p => p.PayrollRunId == run.Id && p.IsIncluded)
            .Select(p => p.EmployeeId).ToListAsync(ct);

        // 2D: materialize attendance penalties into Approved deduction records for the frozen population so
        // they are consumed by the computation below (guaranteed even if "Sync Now" was never run).
        await _attendanceSync.SyncAsync(version, period, frozen, ct: ct);
        var computation = await _computation.ComputeAsync(version, period, frozen, ct);

        // ── Validity exclusions (Task 10 integration) ────────────────────────────
        // For each employee in the frozen population, check structural validity for the period.
        // Also check the DB-only AlreadyInActiveRunForPeriod condition.
        var excludedEmployeeIds = new HashSet<Guid>();
        var exclusionRecords = new List<(Guid EmployeeId, PayrollExclusionReasonCode Reason, string? Detail)>();

        // Load employees to check hire/termination/salary validity.
        var empIds = frozen.ToHashSet();
        var empData = await _db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, e.HireDate, e.TerminationDate, e.BasicSalary })
            .ToListAsync(ct);

        // AlreadyInActiveRunForPeriod: find employees in another non-Cancelled run for the same period
        // (different definition, same tenant).
        var otherActiveRunEmployeeIds = await _db.PayrollRunPopulations.AsNoTracking()
            .Where(p => empIds.Contains(p.EmployeeId)
                        && p.IsIncluded
                        && p.PayrollRunId != run.Id)
            .Join(_db.PayrollRuns.AsNoTracking()
                .Where(r => r.Id != run.Id
                            && r.State != PayrollRunState.Cancelled
                            && r.TargetPeriodYear == run.TargetPeriodYear
                            && r.TargetPeriodMonth == run.TargetPeriodMonth),
                pop => pop.PayrollRunId,
                r => r.Id,
                (pop, r) => pop.EmployeeId)
            .Distinct()
            .ToListAsync(ct);

        var alreadyInOtherRunSet = otherActiveRunEmployeeIds.ToHashSet();

        foreach (var e in empData)
        {
            if (alreadyInOtherRunSet.Contains(e.Id))
            {
                excludedEmployeeIds.Add(e.Id);
                exclusionRecords.Add((e.Id, PayrollExclusionReasonCode.AlreadyInActiveRunForPeriod,
                    $"Employee already in another active run for {run.TargetPeriodYear}-{run.TargetPeriodMonth:D2}"));
                continue;
            }

            var reason = PayrollValidityEvaluator.Evaluate(
                e.HireDate, e.TerminationDate, e.BasicSalary, period.Start, period.End);
            if (reason is not null)
            {
                excludedEmployeeIds.Add(e.Id);
                exclusionRecords.Add((e.Id, reason.Value, null));
            }
        }

        // ── Re-snapshot payslips (only for validity-included employees) ──────────
        var existing = await _db.PayrollPayslips.Where(p => p.PayrollRunId == run.Id).ToListAsync(ct);
        if (existing.Count > 0) _db.PayrollPayslips.RemoveRange(existing);

        // Filter computation results to only include validity-passing employees.
        var includedResults = computation.Results
            .Where(r => !excludedEmployeeIds.Contains(r.EmployeeId))
            .ToList();

        foreach (var r in includedResults)
        {
            _db.PayrollPayslips.Add(new PayrollPayslip
            {
                PayrollRunId = run.Id,
                EmployeeId = r.EmployeeId,
                EmployeeNumber = r.Input.EmployeeNumber,
                EmployeeName = r.Input.EmployeeName,
                Currency = r.Input.Currency,
                GrossEarnings = r.Gross,
                TotalDeductions = r.Deductions,
                NetAmount = r.Net,
                FactsJson = JsonSerializer.Serialize(r.Input.Facts, Json),
                ComponentsJson = JsonSerializer.Serialize(
                    new { order = r.Evaluation.ExecutionOrder, components = r.Evaluation.Components }, Json),
                WarningsJson = r.Warnings.Count > 0 ? JsonSerializer.Serialize(r.Warnings, Json) : null,
            });
        }

        run.EmployeeCount = frozen.Count;
        run.GrossTotal = Math.Round(includedResults.Sum(r => r.Gross), 2);
        run.DeductionTotal = Math.Round(includedResults.Sum(r => r.Deductions), 2);
        run.NetTotal = Math.Round(includedResults.Sum(r => r.Net), 2);

        // ── Validation findings capture (Task 11 integration) ────────────────────
        // Run the validation engine at Calculate time for audit/snapshot purposes only.
        // This does NOT block Calculate — only ValidateAsync gates the run.
        var validationContext = new PayrollValidationContext
        {
            Period = period,
            Currency = run.Currency,
            Inputs = computation.Inputs,
            Results = includedResults,
            RuleCompilation = computation.Compilation,
            OverlappingRuns = Array.Empty<(Guid, DateTime, DateTime)>(),
        };
        var validationReport = _validation.Validate(validationContext);
        var findingsList = validationReport.Findings.ToList();

        // Build summary strings.
        var errorCount = findingsList.Count(f => f.Severity == ValidationSeverity.Error);
        var warnCount  = findingsList.Count(f => f.Severity == ValidationSeverity.Warning);
        var validationSummary = errorCount == 0 && warnCount == 0
            ? "0 findings"
            : $"{errorCount} error{(errorCount != 1 ? "s" : "")}, {warnCount} warning{(warnCount != 1 ? "s" : "")}";
        var topCodes = findingsList.Select(f => f.Code).Distinct().Take(5).ToList();
        var findingSummary = topCodes.Count > 0 ? string.Join(", ", topCodes) : "0 findings";

        // ── Count consumed transactions ───────────────────────────────────────────
        // Re-use the consumer to count how many approved transactions were folded in.
        // Use only validity-included employees so excluded employees don't inflate the count.
        var includedEmployeeIds = frozen.Where(id => !excludedEmployeeIds.Contains(id)).ToList();
        var consumables = await _computation.GetConsumableCountAsync(version, period, includedEmployeeIds, ct);

        // ── Build the versioned snapshot ─────────────────────────────────────────
        var previous = await _db.PayrollRunCalculations
            .Where(c => c.PayrollRunId == run.Id)
            .OrderByDescending(c => c.CalculationVersion)
            .FirstOrDefaultAsync(ct);

        var calcVersion = (previous?.CalculationVersion ?? 0) + 1;
        var includedCount = frozen.Count - excludedEmployeeIds.Count;
        var excludedCount = excludedEmployeeIds.Count;

        var calcAt = DateTime.UtcNow;
        var calc = new PayrollRunCalculation
        {
            PayrollRunId             = run.Id,
            CalculationVersion       = calcVersion,
            CalculatedAt             = calcAt,
            CalculatedByUserId       = Actor,
            PayrollEngineVersion     = run.CalculationVersion,
            PayrollDefinitionVersionId = run.PayrollDefinitionVersionId,
            EmployeeCount            = frozen.Count,
            IncludedEmployees        = includedCount,
            ExcludedEmployees        = excludedCount,
            TransactionCountConsumed = consumables,
            ValidationSummary        = validationSummary,
            FindingSummary           = findingSummary,
            GrossTotal               = run.GrossTotal,
            DeductionTotal           = run.DeductionTotal,
            NetTotal                 = run.NetTotal,
            DurationMs               = (int)sw.ElapsedMilliseconds,
            TriggerSource            = previous is null
                                           ? PayrollCalculationTriggerSource.Manual
                                           : PayrollCalculationTriggerSource.Recalculate,
            PreviousCalculationId    = previous?.Id,
            ChangeSummary            = BuildChangeSummary(previous, consumables, excludedCount, errorCount + warnCount),
        };
        _db.PayrollRunCalculations.Add(calc);

        // Attach finding rows (tagged with calc.Id — assigned by EF on SaveChanges via the nav).
        foreach (var f in findingsList)
        {
            calc.Findings.Add(new PayrollCalculationFinding
            {
                PayrollRunCalculationId = calc.Id,
                Code                    = f.Code,
                Severity                = f.Severity,
                Message                 = f.Message,
                SuggestedAction         = f.SuggestedAction,
                TargetModule            = f.TargetModule,
                TargetScreen            = f.TargetScreen,
                RelatedEntityType       = f.RelatedEntityType,
                RelatedEntityId         = f.RelatedEntityId,
                EmployeeId              = f.EmployeeId,
            });
        }

        // Attach exclusion rows.
        foreach (var (empId, reason, detail) in exclusionRecords)
        {
            calc.Exclusions.Add(new PayrollCalculationExclusion
            {
                PayrollRunCalculationId = calc.Id,
                EmployeeId              = empId,
                ReasonCode              = reason,
                Detail                  = detail,
            });
        }

        // ── Update run calc pointers ─────────────────────────────────────────────
        // NOTE: run.CurrentCalculationVersion is set here from the snapshot version (not +=1)
        // to stay in sync with the PayrollRunCalculation chain.
        run.CurrentCalculationVersion = calcVersion;
        run.LastCalculatedAt          = calcAt;
        run.LastCalculatedByUserId    = Actor;

        // Recalculate always lands in Preview — Draft→Preview (first calc) or
        // Validated/PendingApproval→Preview (recalc invalidates prior validation).
        // Preview→Preview is a no-op transition that the state machine allows (it's the same state,
        // so we only call ApplyTransition when the state actually changes).
        if (run.State != PayrollRunState.Preview)
            ApplyTransition(run, PayrollRunState.Preview, "Calculated");

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("PayrollRunCalculated", nameof(PayrollRun), run.Id,
            null, new { run.EmployeeCount, run.GrossTotal, run.NetTotal, CalcVersion = calcVersion }, ct);
        return run;
    }

    /// <summary>Builds a human-readable delta summary vs the previous snapshot.
    /// "Initial calculation" for version 1; otherwise describes delta in transaction count,
    /// excluded employees, and total finding count.</summary>
    private static string BuildChangeSummary(
        PayrollRunCalculation? previous,
        int consumedCount,
        int excludedCount,
        int findingCount)
    {
        if (previous is null)
            return "Initial calculation";

        var deltaTxn = consumedCount - previous.TransactionCountConsumed;
        var deltaExc = excludedCount - previous.ExcludedEmployees;

        var parts = new List<string>();
        parts.Add($"{deltaTxn:+#;-#;0} transactions consumed");
        parts.Add($"{deltaExc:+#;-#;0} excluded");
        parts.Add($"{findingCount} finding{(findingCount != 1 ? "s" : "")}");
        return string.Join(" · ", parts);
    }

    public async Task<ValidationReport> ValidateAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await LoadRunAsync(runId, ct);
        if (run.State is not (PayrollRunState.Preview or PayrollRunState.Validated))
            throw new InvalidOperationException($"A run can only be validated while Preview or Validated (was {run.State}).");
        await EnsureNotStaleAsync(run, ct);

        var version = await _db.PayrollDefinitionVersions.FirstOrDefaultAsync(v => v.Id == run.PayrollDefinitionVersionId, ct)
            ?? throw new InvalidOperationException("Payroll definition version not found.");
        var period = new PayrollPeriod(run.PeriodStart, run.PeriodEnd);

        // Load the frozen included employee ids — validation must run over the same population as calculate.
        var frozen = await _db.PayrollRunPopulations.AsNoTracking()
            .Where(p => p.PayrollRunId == run.Id && p.IsIncluded)
            .Select(p => p.EmployeeId).ToListAsync(ct);
        var computation = await _computation.ComputeAsync(version, period, frozen, ct);
        var overlapping = await _computation.OverlappingRunsAsync(version.PayrollDefinitionId, period, run.Id, ct);

        var report = _validation.Validate(new PayrollValidationContext
        {
            Period = period,
            Currency = run.Currency,
            Inputs = computation.Inputs,
            Results = computation.Results,
            RuleCompilation = computation.Compilation,
            OverlappingRuns = overlapping,
        });

        run.ValidationResultJson = JsonSerializer.Serialize(report.Findings, Json);
        run.ValidatedAt = DateTime.UtcNow;

        if (report.IsValid && run.State == PayrollRunState.Preview)
            ApplyTransition(run, PayrollRunState.Validated, "Validation passed");

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("PayrollRunValidated", nameof(PayrollRun), run.Id,
            null, new { report.IsValid, errors = report.Errors.Count, warnings = report.Warnings.Count }, ct);
        return report;
    }

    public async Task<PayrollRun> SubmitForApprovalAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await LoadRunAsync(runId, ct);
        await EnsureNotStaleAsync(run, ct);
        ApplyTransition(run, PayrollRunState.PendingApproval, "Submitted for approval");
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync($"PayrollRun{PayrollRunState.PendingApproval}", nameof(PayrollRun),
            run.Id, null, new { to = nameof(PayrollRunState.PendingApproval), reason = "Submitted for approval" }, ct);
        return run;
    }

    public async Task<PayrollRun> ApproveAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await LoadRunAsync(runId, ct);
        await EnsureNotStaleAsync(run, ct);
        ApplyTransition(run, PayrollRunState.Approved, "Approved");
        run.ApprovedByUserId = Actor;
        run.ApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("PayrollRunApproved", nameof(PayrollRun), run.Id, null, new { run.RunNumber }, ct);
        return run;
    }

    public Task<PayrollRun> CancelAsync(Guid runId, string reason, CancellationToken ct = default) =>
        TransitionOnlyAsync(runId, PayrollRunState.Cancelled, reason, ct);

    private async Task<PayrollRun> TransitionOnlyAsync(Guid runId, PayrollRunState to, string? reason, CancellationToken ct)
    {
        var run = await LoadRunAsync(runId, ct);
        ApplyTransition(run, to, reason);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync($"PayrollRun{to}", nameof(PayrollRun), run.Id, null, new { to = to.ToString(), reason }, ct);
        return run;
    }

    private void ApplyTransition(PayrollRun run, PayrollRunState to, string? reason)
    {
        PayrollRunStateMachine.EnsureCanTransition(run.State, to);
        _db.PayrollRunTransitions.Add(new PayrollRunTransition
        {
            PayrollRunId = run.Id,
            FromState = run.State,
            ToState = to,
            At = DateTime.UtcNow,
            ActorUserId = Actor,
            Reason = reason,
        });
        run.State = to;
    }

    /// <summary>Throws <see cref="DomainException"/> with code PAYROLL_RUN_STALE when the run's payslip
    /// snapshot no longer matches the current consumable transaction set. Called at the top of
    /// ValidateAsync, SubmitForApprovalAsync, and ApproveAsync to prevent advancing a stale run.</summary>
    private async Task EnsureNotStaleAsync(PayrollRun run, CancellationToken ct)
    {
        if (await _staleness.IsStaleAsync(run.Id, ct))
            throw new DomainException(
                "PAYROLL_RUN_STALE: the run is stale — Recalculate to include pending transactions before continuing.",
                "PAYROLL_RUN_STALE");
    }

    private async Task<PayrollRun> LoadRunAsync(Guid runId, CancellationToken ct) =>
        await _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
        ?? throw new InvalidOperationException($"Payroll run {runId} not found.");

    private async Task<string> NextRunNumberAsync(CancellationToken ct) =>
        $"PR-{DateTime.UtcNow.Year}-{await _db.PayrollRuns.CountAsync(ct) + 1:D5}";
}
