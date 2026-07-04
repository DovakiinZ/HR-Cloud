using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Finance.Entities;

/// <summary>A payroll run: one execution of a payroll definition over a pay period. It pins the exact
/// definition and rule-set versions used, so the computation is reproducible forever. Its lifecycle is an
/// explicit <see cref="PayrollRunState"/> governed by the PayrollRunStateMachine — never boolean flags —
/// and every transition is recorded in <see cref="Transitions"/>.</summary>
public class PayrollRun : TenantEntity
{
    public string RunNumber { get; set; } = string.Empty;

    public Guid PayrollDefinitionId { get; set; }
    public Guid PayrollDefinitionVersionId { get; set; }
    public Guid? RuleSetVersionId { get; set; }

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    /// <summary>The calendar year of the pay period this run covers. Stamped at creation from the
    /// requested <see cref="PayrollPeriod"/> and never mutated afterward (immutable period identity).</summary>
    public int TargetPeriodYear { get; set; }

    /// <summary>The calendar month (1-12) of the pay period this run covers. Stamped at creation and
    /// never mutated afterward (immutable period identity).</summary>
    public int TargetPeriodMonth { get; set; }

    /// <summary>Incremented by the engine each time <c>CalculateAsync</c> produces a new payslip
    /// snapshot. Starts at 0 (never calculated); first calculate sets it to 1.</summary>
    public int CurrentCalculationVersion { get; set; }

    /// <summary>UTC timestamp of the most recent successful <c>CalculateAsync</c> call. Null until
    /// first calculation.</summary>
    public DateTime? LastCalculatedAt { get; set; }

    /// <summary>The user who triggered the most recent <c>CalculateAsync</c>. Null until first
    /// calculation.</summary>
    public Guid? LastCalculatedByUserId { get; set; }

    public PayrollRunState State { get; set; } = PayrollRunState.Draft;

    public string Currency { get; set; } = "SAR";

    public int EmployeeCount { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal DeductionTotal { get; set; }
    public decimal NetTotal { get; set; }

    /// <summary>Semantic version of the calculation engine that produced the figures.</summary>
    public string CalculationVersion { get; set; } = "1.0";

    public string? Notes { get; set; }

    /// <summary>The validation report captured when the run was validated (jsonb) — part of the snapshot.</summary>
    public string? ValidationResultJson { get; set; }
    public DateTime? ValidatedAt { get; set; }

    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }

    // ── SP6: void + amendment chain ──
    /// <summary>When this run amends another, the id of the run it supersedes.</summary>
    public Guid? AmendsRunId { get; set; }
    /// <summary>On the amended (old) run, the id of the run that superseded it. The AmendsRunId /
    /// SupersededByRunId linked list is the amendment chain / run versioning.</summary>
    public Guid? SupersededByRunId { get; set; }

    public Guid? VoidedByUserId { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidReason { get; set; }

    public ICollection<PayrollRunTransition> Transitions { get; set; } = new List<PayrollRunTransition>();
    public ICollection<PayrollPayslip> Payslips { get; set; } = new List<PayrollPayslip>();
    public ICollection<PayrollRunItem> Items { get; set; } = new List<PayrollRunItem>();
}

/// <summary>An immutable record of one state change in a payroll run's lifecycle — who moved it, when,
/// from which state to which, and why. The run's audit-of-state.</summary>
public class PayrollRunTransition : TenantEntity
{
    public Guid PayrollRunId { get; set; }
    public PayrollRun? Run { get; set; }

    public PayrollRunState FromState { get; set; }
    public PayrollRunState ToState { get; set; }

    public DateTime At { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? Reason { get; set; }
}
