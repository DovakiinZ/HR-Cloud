using HR.Domain.Common;

namespace HR.Domain.Engines.Expenses;

/// <summary>
/// An expense record. Created when an Expense Claim request is approved (the request is the
/// entry point; this is the tracked record shown on the Expenses page). Category references the
/// ExpenseCategory master-data item (governed, no free text).
/// </summary>
public class Expense : TenantEntity
{
    public Guid EmployeeId { get; set; }
    public Guid? ExpenseCategoryId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "SAR";
    public string? Description { get; set; }
    public string? ReceiptUrl { get; set; }
    public string Status { get; set; } = "Approved";   // Approved | Paid | Rejected
    public Guid? RequestInstanceId { get; set; }
    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When true, this expense is picked up by the payroll run whose period covers
    /// <see cref="PayrollMonth"/> and appears as a reimbursement (earning) line on the payslip.</summary>
    public bool IncludeInPayroll { get; set; }

    /// <summary>The payroll month this expense should be paid in (first day of the month, UTC).
    /// Only meaningful when <see cref="IncludeInPayroll"/> is true.</summary>
    public DateTime? PayrollMonth { get; set; }
}
