using System.Text.Json;
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

    public PayrollRunEngine(
        ApplicationDbContext db,
        PayrollComputation computation,
        IPayrollValidationEngine validation,
        ICurrentUserService currentUser,
        IAuditLogService audit,
        IScopeEngine scope,
        IAttendancePayrollSyncService attendanceSync)
    {
        _db = db;
        _computation = computation;
        _validation = validation;
        _currentUser = currentUser;
        _audit = audit;
        _scope = scope;
        _attendanceSync = attendanceSync;
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
        var run = await LoadRunAsync(runId, ct);
        if (run.State is not (PayrollRunState.Draft or PayrollRunState.Preview))
            throw new InvalidOperationException($"A run can only be calculated while Draft or Preview (was {run.State}).");

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

        // Re-snapshot: drop any prior payslips, write fresh immutable snapshots.
        var existing = await _db.PayrollPayslips.Where(p => p.PayrollRunId == run.Id).ToListAsync(ct);
        if (existing.Count > 0) _db.PayrollPayslips.RemoveRange(existing);

        foreach (var r in computation.Results)
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

        run.EmployeeCount = computation.Results.Count;
        run.GrossTotal = Math.Round(computation.Results.Sum(r => r.Gross), 2);
        run.DeductionTotal = Math.Round(computation.Results.Sum(r => r.Deductions), 2);
        run.NetTotal = Math.Round(computation.Results.Sum(r => r.Net), 2);

        // Update calc pointers — immutable period identity (TargetPeriodYear/Month) is NOT touched here.
        run.CurrentCalculationVersion += 1;
        run.LastCalculatedAt = DateTime.UtcNow;
        run.LastCalculatedByUserId = Actor;

        if (run.State == PayrollRunState.Draft)
            ApplyTransition(run, PayrollRunState.Preview, "Calculated");

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("PayrollRunCalculated", nameof(PayrollRun), run.Id,
            null, new { run.EmployeeCount, run.GrossTotal, run.NetTotal }, ct);
        return run;
    }

    public async Task<ValidationReport> ValidateAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await LoadRunAsync(runId, ct);
        if (run.State is not (PayrollRunState.Preview or PayrollRunState.Validated))
            throw new InvalidOperationException($"A run can only be validated while Preview or Validated (was {run.State}).");

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

    public Task<PayrollRun> SubmitForApprovalAsync(Guid runId, CancellationToken ct = default) =>
        TransitionOnlyAsync(runId, PayrollRunState.PendingApproval, "Submitted for approval", ct);

    public async Task<PayrollRun> ApproveAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await LoadRunAsync(runId, ct);
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

    private async Task<PayrollRun> LoadRunAsync(Guid runId, CancellationToken ct) =>
        await _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
        ?? throw new InvalidOperationException($"Payroll run {runId} not found.");

    private async Task<string> NextRunNumberAsync(CancellationToken ct) =>
        $"PR-{DateTime.UtcNow.Year}-{await _db.PayrollRuns.CountAsync(ct) + 1:D5}";
}
