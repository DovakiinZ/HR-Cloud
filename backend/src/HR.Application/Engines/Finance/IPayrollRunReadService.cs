using HR.Application.Common.Paging;

namespace HR.Application.Engines.Finance;

// ── Task 15: row DTOs for the paginated sub-resource endpoints ─────────────────

/// <summary>One included employee row for the /runs/{id}/employees endpoint.
/// Sourced from the frozen PayrollRunPopulation + PayrollPayslip snapshot.</summary>
public sealed record RunEmployeeRow(
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    Guid? DepartmentId,
    decimal Gross,
    decimal Deductions,
    decimal Net,
    bool LedgerPosted);

/// <summary>One excluded employee row for the /runs/{id}/excluded endpoint.
/// Sourced from PayrollCalculationExclusion (latest calc) UNION scope-excluded PayrollRunPopulation rows.</summary>
public sealed record RunExcludedRow(
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    string ReasonCode,
    string? Detail);

/// <summary>One validation finding row for the /runs/{id}/validation endpoint.
/// Sourced from PayrollCalculationFinding rows for the latest calculation.</summary>
public sealed record RunValidationRow(
    string Code,
    string Severity,
    string Message,
    string? SuggestedAction,
    string? TargetModule,
    string? TargetScreen,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    Guid? EmployeeId);

/// <summary>One transaction row for the /runs/{id}/transactions endpoint.
/// Bucket classifies the transaction relative to the run's current payslip snapshot.</summary>
public sealed record RunTransactionRow(
    Guid TransactionId,
    Guid EmployeeId,
    string Kind,
    string TypeCode,
    decimal Amount,
    DateTime EffectiveDate,
    string Status,
    /// <summary>Lifecycle bucket relative to this run: Consumed | ApprovedNotConsumed | PendingApproval | Posted | Reversed.</summary>
    string Bucket);

/// <summary>One calculation history row for the /runs/{id}/calculations list endpoint.</summary>
public sealed record RunCalculationRow(
    int Version,
    DateTime CalculatedAt,
    Guid? ByUserId,
    string TriggerSource,
    int EmployeeCount,
    int IncludedEmployees,
    int ExcludedEmployees,
    int TransactionCountConsumed,
    decimal Gross,
    decimal Deductions,
    decimal Net,
    string ChangeSummary);

/// <summary>Detailed single calculation for the /runs/{id}/calculations/{version} endpoint.</summary>
public sealed record RunCalculationDetail(
    int Version,
    DateTime CalculatedAt,
    Guid? ByUserId,
    string TriggerSource,
    int EmployeeCount,
    int IncludedEmployees,
    int ExcludedEmployees,
    int TransactionCountConsumed,
    decimal Gross,
    decimal Deductions,
    decimal Net,
    string ChangeSummary,
    IReadOnlyList<RunExcludedRow> Exclusions,
    IReadOnlyList<RunValidationRow> Findings);

// ── Task 14: read model ────────────────────────────────────────────────────────

/// <summary>Read model returned by the run SUMMARY endpoint (Task 14).
/// Lightweight: header + server-side KPI aggregates + calc metadata + lifecycle timeline + staleness badge.
/// No employee rows or payslip data (those live on the /employees endpoint, Task 15).</summary>
public sealed record PayrollRunSummary(
    Guid Id,
    string RunNumber,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int TargetPeriodYear,
    int TargetPeriodMonth,
    string State,
    string Currency,
    RunKpis Kpis,
    RunCalcMeta Calc,
    string CalculationStatus,
    IReadOnlyList<RunTransitionSummary> Timeline);

/// <summary>Server-side aggregate KPIs for the run summary card.</summary>
public sealed record RunKpis(
    int IncludedEmployees,
    int ExcludedEmployees,
    decimal Gross,
    decimal Deductions,
    decimal Net,
    int TransactionsConsumed,
    int ApprovedNotConsumed);

/// <summary>Metadata about the latest calculation snapshot pinned to the run.</summary>
public sealed record RunCalcMeta(
    int Version,
    DateTime? At,
    Guid? ByUserId,
    string? ByUserName);

/// <summary>A single lifecycle transition entry on the run timeline.</summary>
public sealed record RunTransitionSummary(
    string FromState,
    string ToState,
    DateTime At,
    string? Reason);

/// <summary>Reads the lightweight run summary with server-side KPI aggregates and staleness status,
/// plus the paginated sub-resources (employees, excluded, validation, transactions, calculations).</summary>
public interface IPayrollRunReadService
{
    /// <summary>Returns the summary for the given run, or null if it does not exist.</summary>
    Task<PayrollRunSummary?> GetSummaryAsync(Guid runId, CancellationToken ct = default);

    // ── Task 15: paginated sub-resources ─────────────────────────────────────

    /// <summary>Included population joined to payslip snapshots, paged. Supports Search on employee name.</summary>
    Task<PagedResult<RunEmployeeRow>> GetEmployeesAsync(Guid runId, PagedRequest request, CancellationToken ct = default);

    /// <summary>Excluded employees: latest-calc PayrollCalculationExclusion rows UNION scope-excluded
    /// PayrollRunPopulation rows (IsIncluded=false). Separate from validation findings.</summary>
    Task<PagedResult<RunExcludedRow>> GetExcludedAsync(Guid runId, PagedRequest request, CancellationToken ct = default);

    /// <summary>Validation findings for the latest calculation snapshot. Separate from excluded rows.</summary>
    Task<PagedResult<RunValidationRow>> GetValidationAsync(Guid runId, PagedRequest request, CancellationToken ct = default);

    /// <summary>Run-scoped transactions with Bucket classification:
    /// Consumed = id in payslip snapshot; ApprovedNotConsumed = Approved ∧ in consumable ∧ not in snapshot;
    /// Posted/Reversed/PendingApproval = by status.</summary>
    Task<PagedResult<RunTransactionRow>> GetTransactionsAsync(Guid runId, PagedRequest request, CancellationToken ct = default);

    /// <summary>Append-only calculation history for the run, ordered by version descending.</summary>
    Task<PagedResult<RunCalculationRow>> GetCalculationsAsync(Guid runId, PagedRequest request, CancellationToken ct = default);

    /// <summary>Single calculation detail (including findings and exclusions), or null if the version does not exist.</summary>
    Task<RunCalculationDetail?> GetCalculationAsync(Guid runId, int version, CancellationToken ct = default);
}
