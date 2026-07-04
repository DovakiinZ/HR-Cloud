namespace HR.Domain.Enums;

/// <summary>Direction of a financial ledger entry. By convention a <see cref="Credit"/> increases the
/// amount owed TO the employee (earnings) and a <see cref="Debit"/> increases the amount owed BY the
/// employee (deductions). Employee net balance = Σ credits − Σ debits.</summary>
public enum LedgerDirection
{
    Debit = 1,
    Credit = 2,
}

/// <summary>Lifecycle marker for a ledger entry. The ledger is append-only: a posting is never mutated.
/// A correction is a new <see cref="Reversal"/> entry that points at the original via ReversesEntryId
/// and carries the opposite direction, so the two net to zero.</summary>
public enum LedgerEntryStatus
{
    Posted = 1,
    Reversal = 2,
}

/// <summary>The business module that originated a financial movement. Everything that touches money
/// becomes a ledger transaction tagged with its source.</summary>
public enum FinanceSourceModule
{
    Manual = 1,
    Attendance = 2,
    Leave = 3,
    Loan = 4,
    Expense = 5,
    Bonus = 6,
    Allowance = 7,
    Commission = 8,
    EndOfService = 9,
    Payroll = 10,
    Adjustment = 11,
    Gosi = 12,
    Tax = 13,
}

/// <summary>Classifies a pay component produced by a rule. Earnings build gross; deductions reduce net;
/// contributions are employer-side costs (e.g. employer GOSI) excluded from net; information components
/// are non-monetary (display/derived values used by other rules).</summary>
public enum PayComponentKind
{
    Earning = 1,
    Deduction = 2,
    Contribution = 3,
    Information = 4,
}

/// <summary>Lifecycle of a payroll definition (the policy object), independent of its versions.</summary>
public enum PayrollDefinitionStatus
{
    Draft = 1,
    Active = 2,
    Suspended = 3,
    Archived = 4,
}

/// <summary>How broadly a payroll definition applies. The concrete population is resolved from the
/// version's employee-filter specification.</summary>
public enum PayrollScope
{
    Company = 1,
    Department = 2,
    Group = 3,
    Custom = 4,
}

/// <summary>State of a single immutable version of a versioned object (definition / rule set / formula).
/// Only one version is <see cref="Published"/> at a time; editing a published version forks a new Draft.</summary>
public enum VersionStatus
{
    Draft = 1,
    Published = 2,
    Superseded = 3,
}

/// <summary>Lifecycle of a rule set (the logical container), independent of its versions.</summary>
public enum RuleSetStatus
{
    Draft = 1,
    Active = 2,
    Archived = 3,
}

/// <summary>Pay cycle frequency for a payroll definition version.</summary>
public enum PayFrequency
{
    Monthly = 1,
    SemiMonthly = 2,
    BiWeekly = 3,
    Weekly = 4,
    Custom = 5,
}

/// <summary>The payroll run lifecycle. Modelled as an explicit state — never a set of boolean flags.
/// Legal transitions are enforced by the PayrollRunStateMachine. Approved/Completed/Locked/Archived are
/// immutable with respect to financial values.</summary>
public enum PayrollRunState
{
    Draft = 1,
    Preview = 2,
    Validated = 3,
    PendingApproval = 4,
    Approved = 5,
    Executing = 6,
    Completed = 7,
    Locked = 8,
    Archived = 9,
    Failed = 10,
    Cancelled = 11,

    /// <summary>A posted run whose ledger postings have been reversed (SP6). Terminal, and — unlike the
    /// Approved+ frozen states — NOT immutable: voiding releases the run's hold on the pay period so an
    /// amendment run can own it. Corrections never reopen a run (ADR PAY-4).</summary>
    Voided = 12,
}

/// <summary>Per-employee processing state inside a batch payroll run — drives concurrency, retries and
/// resume.</summary>
public enum PayrollRunItemState
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    Skipped = 5,
}

/// <summary>How a month's daily wage is prorated.</summary>
public enum DayBasis
{
    CalendarMonth = 1, // basic / actual days in the month
    Fixed30 = 2,       // basic / 30
    WorkingDays = 3,   // basic / working days in the month
}
