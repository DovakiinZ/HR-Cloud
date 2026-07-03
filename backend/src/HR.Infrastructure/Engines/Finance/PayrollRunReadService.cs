using HR.Application.Engines.Finance;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Engines.Finance;

/// <summary>Computes the lightweight run summary (KPI aggregates + calc metadata + staleness status)
/// without materialising any employee rows or payslip data to the caller.
///
/// KPI strategy:
/// - IncludedEmployees / ExcludedEmployees: COUNT from PayrollRunPopulation (always present, never stale).
/// - Gross / Deductions / Net: from run's maintained totals (updated atomically by CalculateAsync; cheapest).
/// - TransactionsConsumed: latest PayrollRunCalculation.TransactionCountConsumed snapshot (written at Calculate).
/// - ApprovedNotConsumed: count of consumable txns NOT in the payslip snapshot — same logic the staleness
///   evaluator uses; computed as a server-side count, never row-materialised to the client.
/// - CalculationStatus: "UpToDate" | "RecalculationRequired" (derived from IPayrollRunStalenessEvaluator).
/// </summary>
public sealed class PayrollRunReadService : IPayrollRunReadService
{
    private readonly ApplicationDbContext _db;
    private readonly IPayrollRunStalenessEvaluator _staleness;
    private readonly IPayrollTransactionConsumer _consumer;

    public PayrollRunReadService(
        ApplicationDbContext db,
        IPayrollRunStalenessEvaluator staleness,
        IPayrollTransactionConsumer consumer)
    {
        _db = db;
        _staleness = staleness;
        _consumer = consumer;
    }

    public async Task<PayrollRunSummary?> GetSummaryAsync(Guid runId, CancellationToken ct = default)
    {
        // Load run header
        var run = await _db.PayrollRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return null;

        // KPI: population counts (server-side COUNT, no row materialisation)
        var includedCount = await _db.PayrollRunPopulations.AsNoTracking()
            .CountAsync(p => p.PayrollRunId == runId && p.IsIncluded, ct);
        var excludedCount = await _db.PayrollRunPopulations.AsNoTracking()
            .CountAsync(p => p.PayrollRunId == runId && !p.IsIncluded, ct);

        // KPI: transactions consumed — from the latest calc snapshot
        var latestCalc = await _db.PayrollRunCalculations.AsNoTracking()
            .Where(c => c.PayrollRunId == runId)
            .OrderByDescending(c => c.CalculationVersion)
            .Select(c => new { c.CalculationVersion, c.CalculatedAt, c.CalculatedByUserId, c.TransactionCountConsumed })
            .FirstOrDefaultAsync(ct);

        var transactionsConsumed = latestCalc?.TransactionCountConsumed ?? 0;

        // KPI: ApprovedNotConsumed — count of consumable txns not reflected in the current payslip snapshot.
        // This mirrors the staleness evaluator's forward-gap check but returns a count, not a bool.
        var approvedNotConsumed = await ComputeApprovedNotConsumedAsync(runId, run, ct);

        // CalculationStatus — delegate to the staleness evaluator (consistent with gate logic)
        var isStale = run.CurrentCalculationVersion > 0
            && await _staleness.IsStaleAsync(runId, ct);
        var calculationStatus = isStale ? "RecalculationRequired" : "UpToDate";

        // Calc metadata
        var calcMeta = new RunCalcMeta(
            latestCalc?.CalculationVersion ?? 0,
            latestCalc is not null ? latestCalc.CalculatedAt : (DateTime?)null,
            latestCalc?.CalculatedByUserId,
            ByUserName: null);   // user-name lookup skipped (expensive join; null is documented as acceptable)

        // Timeline
        var timeline = await _db.PayrollRunTransitions.AsNoTracking()
            .Where(t => t.PayrollRunId == runId)
            .OrderBy(t => t.At)
            .Select(t => new RunTransitionSummary(
                t.FromState.ToString(), t.ToState.ToString(), t.At, t.Reason))
            .ToListAsync(ct);

        return new PayrollRunSummary(
            Id: run.Id,
            RunNumber: run.RunNumber,
            PeriodStart: run.PeriodStart,
            PeriodEnd: run.PeriodEnd,
            TargetPeriodYear: run.TargetPeriodYear,
            TargetPeriodMonth: run.TargetPeriodMonth,
            State: run.State.ToString(),
            Currency: run.Currency,
            Kpis: new RunKpis(
                IncludedEmployees: includedCount,
                ExcludedEmployees: excludedCount,
                Gross: run.GrossTotal,
                Deductions: run.DeductionTotal,
                Net: run.NetTotal,
                TransactionsConsumed: transactionsConsumed,
                ApprovedNotConsumed: approvedNotConsumed),
            Calc: calcMeta,
            CalculationStatus: calculationStatus,
            Timeline: timeline);
    }

    /// <summary>Counts approved in-period consumable transactions that are NOT already reflected in the
    /// current payslip snapshot. Single aggregate path — no row materialisation to caller.</summary>
    private async Task<int> ComputeApprovedNotConsumedAsync(
        Guid runId, HR.Domain.Engines.Finance.Entities.PayrollRun run, CancellationToken ct)
    {
        if (run.CurrentCalculationVersion == 0)
            return 0; // never calculated — nothing to compare

        // Load the version for cutoff config
        var ver = await _db.PayrollDefinitionVersions.AsNoTracking()
            .Where(v => v.Id == run.PayrollDefinitionVersionId)
            .Select(v => new { v.CutoffDay, v.CarryToNextPeriod })
            .FirstOrDefaultAsync(ct);
        if (ver is null) return 0;

        // Included employee ids (from frozen population snapshot)
        var empIds = await _db.PayrollRunPopulations.AsNoTracking()
            .Where(p => p.PayrollRunId == runId && p.IsIncluded)
            .Select(p => p.EmployeeId)
            .ToListAsync(ct);
        if (empIds.Count == 0) return 0;

        // Consumable set (approved, in-period, in-population)
        var consumable = await _consumer.GetConsumableAsync(
            run.TargetPeriodYear, run.TargetPeriodMonth,
            empIds, ver.CutoffDay, ver.CarryToNextPeriod, ct);

        if (consumable.Count == 0) return 0;

        // TXN ids already in the payslip snapshot
        var componentJsonList = await _db.PayrollPayslips.AsNoTracking()
            .Where(p => p.PayrollRunId == runId)
            .Select(p => p.ComponentsJson)
            .ToListAsync(ct);

        var snapshotTxnIds = componentJsonList
            .SelectMany(PayrollRunStalenessEvaluator.ParseTxnIdsPublic)
            .ToHashSet();

        // Forward gap: consumable txns NOT in the snapshot
        return consumable.Count(c => !snapshotTxnIds.Contains(c.TransactionId));
    }
}
