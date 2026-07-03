namespace HR.Modules.Payroll.DTOs;

public class PayrollDefinitionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? CurrentVersionId { get; set; }
    public string Currency { get; set; } = "SAR";
}

public class PayrollRunListItem
{
    public Guid Id { get; set; }
    public string RunNumber { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string State { get; set; } = string.Empty;
    public string Currency { get; set; } = "SAR";
    public int EmployeeCount { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal DeductionTotal { get; set; }
    public decimal NetTotal { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PayslipDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Currency { get; set; } = "SAR";
    public decimal GrossEarnings { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetAmount { get; set; }
    public bool LedgerPosted { get; set; }
    public string? ComponentsJson { get; set; }
}

public class ValidationFindingDto
{
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    /// <summary>Actionable guidance for the payroll administrator to resolve this finding.</summary>
    public string? SuggestedAction { get; set; }
    /// <summary>Front-end module the user should navigate to (e.g. "Employees", "Payroll", "Attendance").</summary>
    public string? TargetModule { get; set; }
    /// <summary>Specific screen within TargetModule (e.g. "employee-profile", "run", "daily").</summary>
    public string? TargetScreen { get; set; }
    /// <summary>Domain object type this finding concerns (e.g. "Employee").</summary>
    public string? RelatedEntityType { get; set; }
    /// <summary>PK of the related entity for deep-linking.</summary>
    public Guid? RelatedEntityId { get; set; }
    public Guid? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
}

public class RunTransitionDto
{
    public string FromState { get; set; } = string.Empty;
    public string ToState { get; set; } = string.Empty;
    public DateTime At { get; set; }
    public string? Reason { get; set; }
}

public class PayrollRunDetail : PayrollRunListItem
{
    public Guid PayrollDefinitionId { get; set; }
    public Guid PayrollDefinitionVersionId { get; set; }
    public Guid? RuleSetVersionId { get; set; }
    public string? Notes { get; set; }
    public DateTime? ValidatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public List<PayslipDto> Payslips { get; set; } = new();
    public List<ValidationFindingDto> Validation { get; set; } = new();
    public List<RunTransitionDto> Transitions { get; set; } = new();
}

public class CreateRunRequest
{
    public Guid DefinitionId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
}

public class PreviewRequest
{
    public Guid DefinitionId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
}

public class PayrollPreviewLineDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public decimal Gross { get; set; }
    public decimal Deductions { get; set; }
    public decimal Net { get; set; }
    public bool HasErrors { get; set; }
}

public class PayrollPreviewDto
{
    public int EmployeeCount { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal DeductionTotal { get; set; }
    public decimal NetTotal { get; set; }
    public string Currency { get; set; } = "SAR";
    public bool IsValid { get; set; }
    public List<ValidationFindingDto> Findings { get; set; } = new();
    public List<PayrollPreviewLineDto> Lines { get; set; } = new();
}

public class CancelRunRequest { public string Reason { get; set; } = string.Empty; }

// ── Task 14: lightweight run SUMMARY DTOs ──────────────────────────────────────────────────────────

/// <summary>Server-side KPI aggregates returned by the run summary endpoint — no employee rows materialised.</summary>
public class RunKpisDto
{
    public int IncludedEmployees { get; set; }
    public int ExcludedEmployees { get; set; }
    public decimal Gross { get; set; }
    public decimal Deductions { get; set; }
    public decimal Net { get; set; }
    public int TransactionsConsumed { get; set; }
    public int ApprovedNotConsumed { get; set; }
}

/// <summary>Metadata about the latest calculation snapshot pinned to the run.</summary>
public class RunCalcMetaDto
{
    public int Version { get; set; }
    public DateTime? At { get; set; }
    public Guid? ByUserId { get; set; }
    public string? ByUserName { get; set; }
}

/// <summary>Lightweight decomposed run summary: header + KPI cards + calc metadata + lifecycle timeline
/// + calc-status badge. No inline payslips (those live on /employees, Task 15).</summary>
public class PayrollRunSummaryDto
{
    public Guid Id { get; set; }
    public string RunNumber { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int TargetPeriodYear { get; set; }
    public int TargetPeriodMonth { get; set; }
    public string State { get; set; } = string.Empty;
    public string Currency { get; set; } = "SAR";

    /// <summary>Server-side KPI aggregates (COUNT/SUM — never row-materialised).</summary>
    public RunKpisDto Kpis { get; set; } = new();

    /// <summary>Latest calculation snapshot metadata.</summary>
    public RunCalcMetaDto Calc { get; set; } = new();

    /// <summary>"UpToDate" or "RecalculationRequired" (server-derived from staleness evaluator).</summary>
    public string CalculationStatus { get; set; } = string.Empty;

    /// <summary>Ordered lifecycle transitions for the run timeline.</summary>
    public List<RunTransitionDto> Timeline { get; set; } = new();
}
