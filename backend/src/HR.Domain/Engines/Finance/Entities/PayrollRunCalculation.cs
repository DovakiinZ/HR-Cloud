using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Finance.Entities;

/// <summary>An immutable, append-only snapshot of a single Calculate (or Recalculate) run.
/// Every call to <c>PayrollRunEngine.CalculateAsync</c> appends one row here with a monotonically
/// increasing <see cref="CalculationVersion"/>, a back-pointer <see cref="PreviousCalculationId"/>,
/// rich metadata (totals, counts, engine version, trigger), and a human-readable
/// <see cref="ChangeSummary"/> of what changed since the previous version.
///
/// Child rows in <see cref="PayrollCalculationFinding"/> and <see cref="PayrollCalculationExclusion"/>
/// are tagged with this record's Id for per-calculation audit.
///
/// Append-only: this record is NEVER updated or deleted after creation.</summary>
public class PayrollRunCalculation : TenantEntity
{
    /// <summary>The payroll run this snapshot belongs to.</summary>
    public Guid PayrollRunId { get; set; }

    /// <summary>Monotonically increasing counter per run. Starts at 1 for the first Calculate call,
    /// increments by 1 on each Recalculate. Never reused (even if a previous snapshot is deleted).</summary>
    public int CalculationVersion { get; set; }

    /// <summary>UTC timestamp when this calculation was produced.</summary>
    public DateTime CalculatedAt { get; set; }

    /// <summary>User who triggered this calculate. Null for system-automated calculations.</summary>
    public Guid? CalculatedByUserId { get; set; }

    /// <summary>Semantic version of the calculation algorithm/engine that produced the figures
    /// (from <see cref="PayrollRun.CalculationVersion"/>). Allows correlating bugs to engine versions.</summary>
    public string PayrollEngineVersion { get; set; } = string.Empty;

    /// <summary>The exact payroll definition version used for this calculation — pins the rule set,
    /// currency, scope, and cycle configuration that produced these figures.</summary>
    public Guid PayrollDefinitionVersionId { get; set; }

    /// <summary>Total number of employees in scope (included + excluded) at Calculate time.</summary>
    public int EmployeeCount { get; set; }

    /// <summary>Employees actually computed and given a payslip in this snapshot.</summary>
    public int IncludedEmployees { get; set; }

    /// <summary>Employees present in scope but structurally excluded (not employed in period,
    /// no active salary, already in another active run for the period, etc.).</summary>
    public int ExcludedEmployees { get; set; }

    /// <summary>Count of approved addition/deduction <c>PayrollTransaction</c> records consumed
    /// (folded into payslip components) by this calculation.</summary>
    public int TransactionCountConsumed { get; set; }

    /// <summary>Aggregated validation finding summary string (e.g. "2 errors, 1 warning").
    /// Populated by running the validation engine at Calculate time (non-blocking — findings are
    /// recorded for audit; only <c>ValidateAsync</c> gates the run).</summary>
    public string ValidationSummary { get; set; } = string.Empty;

    /// <summary>Top finding codes (e.g. "NEGATIVE_SALARY, MISSING_ATTENDANCE") for quick scanning
    /// without loading child <see cref="PayrollCalculationFinding"/> rows.</summary>
    public string FindingSummary { get; set; } = string.Empty;

    /// <summary>Sum of gross earnings across all included employees in this snapshot.</summary>
    public decimal GrossTotal { get; set; }

    /// <summary>Sum of all deductions across all included employees in this snapshot.</summary>
    public decimal DeductionTotal { get; set; }

    /// <summary>Sum of net pay (Gross − Deductions) across all included employees.</summary>
    public decimal NetTotal { get; set; }

    /// <summary>Wall-clock time in milliseconds taken by the CalculateAsync call.
    /// Used for performance trending and capacity planning.</summary>
    public int DurationMs { get; set; }

    /// <summary>What triggered this calculation: <see cref="PayrollCalculationTriggerSource.Manual"/>
    /// for the first Calculate (user-initiated), <see cref="PayrollCalculationTriggerSource.Recalculate"/>
    /// for subsequent recalculations.</summary>
    public PayrollCalculationTriggerSource TriggerSource { get; set; }

    /// <summary>Id of the immediately preceding <see cref="PayrollRunCalculation"/> for this run,
    /// forming a linked-list chain from latest to oldest. Null for version 1.</summary>
    public Guid? PreviousCalculationId { get; set; }

    /// <summary>Short human-readable description of what changed vs the previous calculation
    /// (e.g. "+2 transactions consumed · +1 excluded · 0 findings"). "Initial calculation" for
    /// version 1.</summary>
    public string ChangeSummary { get; set; } = string.Empty;

    // Navigation properties
    public ICollection<PayrollCalculationFinding> Findings { get; set; } = new List<PayrollCalculationFinding>();
    public ICollection<PayrollCalculationExclusion> Exclusions { get; set; } = new List<PayrollCalculationExclusion>();
}
