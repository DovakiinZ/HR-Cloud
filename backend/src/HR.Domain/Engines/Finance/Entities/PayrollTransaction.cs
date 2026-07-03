using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Finance.Entities;

/// <summary>A single dated payroll addition or deduction. Distinct from the recurring per-employee
/// EmployeeAddition/EmployeeDeduction components: this is a traceable, approvable, period-bound record that
/// exists before payroll runs. Sub-project 2A manages it up to Approved; the engine consumes it in 2B.</summary>
public class PayrollTransaction : TenantEntity
{
    /// <summary>Addition or deduction. Sign is implied by Kind; <see cref="Amount"/> is always non-negative.</summary>
    public PayrollTransactionKind Kind { get; set; }

    public Guid EmployeeId { get; set; }

    /// <summary>References a MasterDataItem whose ObjectType is "AdditionType" (Kind=Addition) or
    /// "DeductionType" (Kind=Deduction).</summary>
    public Guid TypeId { get; set; }

    /// <summary>Non-negative amount in the tenant currency.</summary>
    public decimal Amount { get; set; }

    /// <summary>When the business event occurred (e.g. the day the bonus/penalty was decided).</summary>
    public DateTime TransactionDate { get; set; }

    /// <summary>The date that drives payroll-period selection and (in 2B) cutoff. All business calculation
    /// uses this date.</summary>
    public DateTime EffectiveDate { get; set; }

    /// <summary>Intended payroll period (year), derived from EffectiveDate on create. Display-only in 2A.</summary>
    public int? TargetPeriodYear { get; set; }

    /// <summary>Intended payroll period (month 1-12), derived from EffectiveDate on create.</summary>
    public int? TargetPeriodMonth { get; set; }

    /// <summary>Flag only in 2A; per-period materialization is implemented in 2B.</summary>
    public bool IsRecurring { get; set; }
    public DateTime? RecurrenceEndDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>Optional StoredFile id, served via /api/files/{id}.</summary>
    public Guid? AttachmentFileId { get; set; }

    /// <summary>Provenance: "Manual" in 2A; "Attendance"/"Loan"/... set by source modules in 2B/2C.</summary>
    public string SourceModule { get; set; } = "Manual";

    /// <summary>Traceability back to an originating record (e.g. "AttendanceRecord").</summary>
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }

    public PayrollTransactionStatus Status { get; set; } = PayrollTransactionStatus.Draft;

    /// <summary>Reason captured on reject/cancel/reversal.</summary>
    public string? StatusReason { get; set; }

    // --- Posting metadata: columns defined in 2A, populated by the engine in 2B. ---
    public Guid? PayrollRunId { get; set; }
    public DateTime? PostedAt { get; set; }
    public Guid? PostedBy { get; set; }
    public Guid? LedgerEntryId { get; set; }

    // --- Reversal link: defined in 2A, transition wired in 2B. ---
    public Guid? ReversesTransactionId { get; set; }
    public string? ReversalReason { get; set; }

    // --- Audit provenance (D10, SP3 Task 9). ---
    /// <summary>UI/API surface that created the transaction. Distinct from SourceModule (business system).
    /// Always non-nullable; defaults to System so existing rows backfill cleanly.</summary>
    public PayrollTransactionOrigin Origin { get; set; } = PayrollTransactionOrigin.System;

    /// <summary>The payroll run from which this transaction was created, if any (e.g. via RunPage).
    /// Permanent; not changed when the run that consumes/posts it is set on PayrollRunId.</summary>
    public Guid? CreatedFromRunId { get; set; }
}
