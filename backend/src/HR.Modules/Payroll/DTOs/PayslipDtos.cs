namespace HR.Modules.Payroll.DTOs;

/// <summary>A required reason for a run-lifecycle action (void / amend).</summary>
public sealed class RunReasonRequest
{
    public string Reason { get; set; } = string.Empty;
}

/// <summary>One row of a run's payslip list: the employee identity + totals from the frozen snapshot, and
/// whether an immutable PDF has been archived yet.</summary>
public sealed class PayslipRowDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Currency { get; set; } = "SAR";
    public decimal GrossEarnings { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetAmount { get; set; }
    public bool Archived { get; set; }
}
