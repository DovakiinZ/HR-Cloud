using HR.Application.Common.Paging;
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance;
using HR.Domain.Enums;
using HR.Infrastructure.Common.Paging;
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

    // ── Task 15: paginated sub-resource read methods ─────────────────────────────

    public async Task<PagedResult<RunEmployeeRow>> GetEmployeesAsync(
        Guid runId, PagedRequest request, CancellationToken ct = default)
    {
        // Join population (IsIncluded=true) to payslip snapshot.
        var query = _db.PayrollRunPopulations.AsNoTracking()
            .Where(p => p.PayrollRunId == runId && p.IsIncluded)
            .Join(_db.PayrollPayslips.AsNoTracking().Where(s => s.PayrollRunId == runId),
                pop => pop.EmployeeId,
                slip => slip.EmployeeId,
                (pop, slip) => new RunEmployeeRow(
                    pop.EmployeeId,
                    pop.EmployeeNumber,
                    pop.EmployeeName,
                    pop.DepartmentId,
                    slip.GrossEarnings,
                    slip.TotalDeductions,
                    slip.NetAmount,
                    slip.LedgerPosted));

        // Apply search on employee name (case-insensitive contains).
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(r => r.EmployeeName.ToLower().Contains(search)
                                  || r.EmployeeNumber.ToLower().Contains(search));
        }

        return await query.ToPagedResultAsync(request, ct);
    }

    public async Task<PagedResult<RunExcludedRow>> GetExcludedAsync(
        Guid runId, PagedRequest request, CancellationToken ct = default)
    {
        // Strategy: build two sources and union them.
        // Source A: latest PayrollCalculationExclusion rows.
        var latestCalcId = await _db.PayrollRunCalculations.AsNoTracking()
            .Where(c => c.PayrollRunId == runId)
            .OrderByDescending(c => c.CalculationVersion)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        // Source B: scope-excluded population rows (IsIncluded=false) not already in Source A.
        // We union in memory after querying both.
        var calcExclusions = latestCalcId.HasValue
            ? await _db.PayrollCalculationExclusions.AsNoTracking()
                .Where(e => e.PayrollRunCalculationId == latestCalcId.Value)
                .Select(e => new RunExcludedRow(
                    e.EmployeeId,
                    string.Empty, // name not on entity; looked up below if needed
                    string.Empty,
                    e.ReasonCode.ToString(),
                    e.Detail))
                .ToListAsync(ct)
            : new List<RunExcludedRow>();

        // Get names for calc exclusions from the population snapshot.
        var calcEmpIds = calcExclusions.Select(r => r.EmployeeId).ToHashSet();
        var popNames = await _db.PayrollRunPopulations.AsNoTracking()
            .Where(p => p.PayrollRunId == runId && calcEmpIds.Contains(p.EmployeeId))
            .ToDictionaryAsync(p => p.EmployeeId, p => (p.EmployeeNumber, p.EmployeeName), ct);

        var calcRows = calcExclusions.Select(r =>
        {
            var (num, name) = popNames.TryGetValue(r.EmployeeId, out var t) ? t : (string.Empty, string.Empty);
            return r with { EmployeeNumber = num, EmployeeName = name };
        }).ToList();

        // Scope-excluded population rows (not already covered by calc exclusions).
        var scopeRows = await _db.PayrollRunPopulations.AsNoTracking()
            .Where(p => p.PayrollRunId == runId && !p.IsIncluded
                     && !calcEmpIds.Contains(p.EmployeeId))
            .Select(p => new RunExcludedRow(
                p.EmployeeId,
                p.EmployeeNumber,
                p.EmployeeName,
                p.ExclusionReasonCode ?? "ExcludedByScope",
                null))
            .ToListAsync(ct);

        // Merge and apply paging in memory (both sets are typically small).
        var all = calcRows.Concat(scopeRows)
            .OrderBy(r => r.EmployeeName)
            .AsQueryable();

        return await all.ToPagedResultAsync(request, ct);
    }

    public async Task<PagedResult<RunValidationRow>> GetValidationAsync(
        Guid runId, PagedRequest request, CancellationToken ct = default)
    {
        var latestCalcId = await _db.PayrollRunCalculations.AsNoTracking()
            .Where(c => c.PayrollRunId == runId)
            .OrderByDescending(c => c.CalculationVersion)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        if (!latestCalcId.HasValue)
            return new PagedResult<RunValidationRow>(Array.Empty<RunValidationRow>(), 1, request.PageSize, 0);

        var query = _db.PayrollCalculationFindings.AsNoTracking()
            .Where(f => f.PayrollRunCalculationId == latestCalcId.Value)
            .OrderBy(f => f.Severity)
            .ThenBy(f => f.Code)
            .Select(f => new RunValidationRow(
                f.Code,
                f.Severity.ToString(),
                f.Message,
                f.SuggestedAction,
                f.TargetModule,
                f.TargetScreen,
                f.RelatedEntityType,
                f.RelatedEntityId,
                f.EmployeeId));

        return await query.ToPagedResultAsync(request, ct);
    }

    public async Task<PagedResult<RunTransactionRow>> GetTransactionsAsync(
        Guid runId, PagedRequest request, CancellationToken ct = default)
    {
        // Load the run for period + version info.
        var run = await _db.PayrollRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
            return new PagedResult<RunTransactionRow>(Array.Empty<RunTransactionRow>(), 1, request.PageSize, 0);

        // Load version config for cutoff parameters.
        var ver = await _db.PayrollDefinitionVersions.AsNoTracking()
            .Where(v => v.Id == run.PayrollDefinitionVersionId)
            .Select(v => new { v.CutoffDay, v.CarryToNextPeriod })
            .FirstOrDefaultAsync(ct);
        if (ver is null)
            return new PagedResult<RunTransactionRow>(Array.Empty<RunTransactionRow>(), 1, request.PageSize, 0);

        // Included employee ids.
        var empIds = await _db.PayrollRunPopulations.AsNoTracking()
            .Where(p => p.PayrollRunId == runId && p.IsIncluded)
            .Select(p => p.EmployeeId)
            .ToListAsync(ct);

        // Consumable set (Approved, in-period, in-population).
        var consumable = empIds.Count > 0
            ? await _consumer.GetConsumableAsync(
                run.TargetPeriodYear, run.TargetPeriodMonth,
                empIds, ver.CutoffDay, ver.CarryToNextPeriod, ct)
            : (IReadOnlyList<ConsumableTransaction>)Array.Empty<ConsumableTransaction>();

        var consumableIds = consumable.Select(c => c.TransactionId).ToHashSet();

        // Snapshot TXN ids (already in payslip components).
        var componentJsonList = await _db.PayrollPayslips.AsNoTracking()
            .Where(p => p.PayrollRunId == runId)
            .Select(p => p.ComponentsJson)
            .ToListAsync(ct);

        var snapshotTxnIds = componentJsonList
            .SelectMany(PayrollRunStalenessEvaluator.ParseTxnIdsPublic)
            .ToHashSet();

        // Load all relevant transactions:
        // - Consumable (Approved, in-period, in-population)  → ApprovedNotConsumed or Consumed
        // - Posted/Reversed for this run                     → Posted or Reversed
        // - PendingApproval for the population in-period     → PendingApproval
        var allIds = consumableIds
            .Union(snapshotTxnIds)
            .ToHashSet();

        // Also include Posted/Reversed transactions linked to this run.
        var postedIds = await _db.PayrollTransactions.AsNoTracking()
            .Where(t => t.PayrollRunId == runId
                     && (t.Status == PayrollTransactionStatus.Posted || t.Status == PayrollTransactionStatus.Reversed))
            .Select(t => t.Id)
            .ToListAsync(ct);

        allIds.UnionWith(postedIds);

        // And in-period PendingApproval transactions for the population.
        if (empIds.Count > 0)
        {
            var pendingIds = await _db.PayrollTransactions.AsNoTracking()
                .Where(t => t.Status == PayrollTransactionStatus.PendingApproval
                         && empIds.Contains(t.EmployeeId))
                .Select(t => t.Id)
                .ToListAsync(ct);

            // Filter to in-period using cutoff rule.
            var pendingWithDates = await _db.PayrollTransactions.AsNoTracking()
                .Where(t => pendingIds.Contains(t.Id))
                .Select(t => new { t.Id, t.EffectiveDate })
                .ToListAsync(ct);

            foreach (var p in pendingWithDates)
            {
                var (py, pm) = PayrollPeriodResolver.Resolve(p.EffectiveDate, ver.CutoffDay, ver.CarryToNextPeriod);
                if (py == run.TargetPeriodYear && pm == run.TargetPeriodMonth)
                    allIds.Add(p.Id);
            }
        }

        if (allIds.Count == 0)
            return new PagedResult<RunTransactionRow>(Array.Empty<RunTransactionRow>(), 1, request.PageSize, 0);

        // Load full transaction data for bucketing.
        var txns = await _db.PayrollTransactions.AsNoTracking()
            .Where(t => allIds.Contains(t.Id))
            .Select(t => new { t.Id, t.EmployeeId, t.Kind, t.TypeId, t.Amount, t.EffectiveDate, t.Status })
            .ToListAsync(ct);

        // Resolve type codes.
        var typeIds = txns.Select(t => t.TypeId).Distinct().ToList();
        var typeCodes = await _db.MasterDataItems.AsNoTracking()
            .Where(m => typeIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Code, ct);

        // Bucket assignment.
        string Bucket(Guid id, PayrollTransactionStatus status)
        {
            if (snapshotTxnIds.Contains(id))       return "Consumed";
            if (status == PayrollTransactionStatus.Posted)    return "Posted";
            if (status == PayrollTransactionStatus.Reversed)  return "Reversed";
            if (status == PayrollTransactionStatus.PendingApproval) return "PendingApproval";
            if (status == PayrollTransactionStatus.Approved && consumableIds.Contains(id))
                return "ApprovedNotConsumed";
            return "Other";
        }

        var rows = txns
            .Select(t => new RunTransactionRow(
                t.Id,
                t.EmployeeId,
                t.Kind.ToString(),
                typeCodes.TryGetValue(t.TypeId, out var code) ? code : "TXN",
                t.Amount,
                t.EffectiveDate,
                t.Status.ToString(),
                Bucket(t.Id, t.Status)))
            .OrderByDescending(r => r.EffectiveDate)
            .AsQueryable();

        return await rows.ToPagedResultAsync(request, ct);
    }

    public async Task<PagedResult<RunCalculationRow>> GetCalculationsAsync(
        Guid runId, PagedRequest request, CancellationToken ct = default)
    {
        var query = _db.PayrollRunCalculations.AsNoTracking()
            .Where(c => c.PayrollRunId == runId)
            .OrderByDescending(c => c.CalculationVersion)
            .Select(c => new RunCalculationRow(
                c.CalculationVersion,
                c.CalculatedAt,
                c.CalculatedByUserId,
                c.TriggerSource.ToString(),
                c.EmployeeCount,
                c.IncludedEmployees,
                c.ExcludedEmployees,
                c.TransactionCountConsumed,
                c.GrossTotal,
                c.DeductionTotal,
                c.NetTotal,
                c.ChangeSummary));

        return await query.ToPagedResultAsync(request, ct);
    }

    public async Task<RunCalculationDetail?> GetCalculationAsync(
        Guid runId, int version, CancellationToken ct = default)
    {
        var calc = await _db.PayrollRunCalculations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.PayrollRunId == runId && c.CalculationVersion == version, ct);
        if (calc is null) return null;

        var exclusions = await _db.PayrollCalculationExclusions.AsNoTracking()
            .Where(e => e.PayrollRunCalculationId == calc.Id)
            .Select(e => new RunExcludedRow(
                e.EmployeeId, string.Empty, string.Empty,
                e.ReasonCode.ToString(), e.Detail))
            .ToListAsync(ct);

        // Enrich exclusions with names from population.
        var excEmpIds = exclusions.Select(r => r.EmployeeId).ToHashSet();
        var popNames = await _db.PayrollRunPopulations.AsNoTracking()
            .Where(p => p.PayrollRunId == runId && excEmpIds.Contains(p.EmployeeId))
            .ToDictionaryAsync(p => p.EmployeeId, p => (p.EmployeeNumber, p.EmployeeName), ct);

        var enrichedExclusions = exclusions
            .Select(r =>
            {
                var (num, name) = popNames.TryGetValue(r.EmployeeId, out var t) ? t : (string.Empty, string.Empty);
                return r with { EmployeeNumber = num, EmployeeName = name };
            })
            .ToList();

        var findings = await _db.PayrollCalculationFindings.AsNoTracking()
            .Where(f => f.PayrollRunCalculationId == calc.Id)
            .OrderBy(f => f.Severity).ThenBy(f => f.Code)
            .Select(f => new RunValidationRow(
                f.Code, f.Severity.ToString(), f.Message, f.SuggestedAction,
                f.TargetModule, f.TargetScreen, f.RelatedEntityType, f.RelatedEntityId, f.EmployeeId))
            .ToListAsync(ct);

        return new RunCalculationDetail(
            calc.CalculationVersion, calc.CalculatedAt, calc.CalculatedByUserId,
            calc.TriggerSource.ToString(),
            calc.EmployeeCount, calc.IncludedEmployees, calc.ExcludedEmployees,
            calc.TransactionCountConsumed,
            calc.GrossTotal, calc.DeductionTotal, calc.NetTotal,
            calc.ChangeSummary,
            enrichedExclusions, findings);
    }

    // ── Task 14: private helpers ──────────────────────────────────────────────────

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
