namespace HR.Application.Engines.Finance;

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

/// <summary>Reads the lightweight run summary with server-side KPI aggregates and staleness status.</summary>
public interface IPayrollRunReadService
{
    /// <summary>Returns the summary for the given run, or null if it does not exist.</summary>
    Task<PayrollRunSummary?> GetSummaryAsync(Guid runId, CancellationToken ct = default);
}
