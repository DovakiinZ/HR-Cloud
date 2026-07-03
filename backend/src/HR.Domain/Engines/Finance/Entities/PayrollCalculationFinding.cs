using HR.Domain.Common;
using HR.Domain.Engines.Finance;

namespace HR.Domain.Engines.Finance.Entities;

/// <summary>Persisted record of a single validation finding produced during a payroll calculation.
/// Mirrors the in-memory <see cref="ValidationFinding"/> but as a first-class EF entity so
/// findings are queryable per run without re-parsing the legacy ValidationResultJson blob.
///
/// EF wiring (DbSet, FK to PayrollRunCalculation, index, migration) is added in Task 12, which
/// owns all calculation-table EF configuration. This file is the POCO class only.</summary>
public class PayrollCalculationFinding : TenantEntity
{
    /// <summary>The calculation snapshot this finding belongs to.
    /// FK and navigation to PayrollRunCalculation are configured in Task 12.</summary>
    public Guid PayrollRunCalculationId { get; set; }

    /// <summary>Machine-readable, stable finding code (e.g. "NEGATIVE_SALARY", "MISSING_ATTENDANCE").</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Severity level. Only <see cref="ValidationSeverity.Error"/> blocks payroll approval.</summary>
    public ValidationSeverity Severity { get; set; }

    /// <summary>Human-readable description of the finding.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Actionable guidance for the payroll administrator to resolve this finding.</summary>
    public string? SuggestedAction { get; set; }

    /// <summary>Front-end module the user should navigate to in order to resolve this finding
    /// (e.g. "Employees", "Payroll", "Attendance").</summary>
    public string? TargetModule { get; set; }

    /// <summary>Specific screen/page within <see cref="TargetModule"/>
    /// (e.g. "employee-profile", "run", "daily").</summary>
    public string? TargetScreen { get; set; }

    /// <summary>Domain object type the finding relates to (e.g. "Employee", "PayrollRun").
    /// Used with <see cref="RelatedEntityId"/> to deep-link into the correct record.</summary>
    public string? RelatedEntityType { get; set; }

    /// <summary>PK of the <see cref="RelatedEntityType"/> record this finding concerns.</summary>
    public Guid? RelatedEntityId { get; set; }

    /// <summary>Employee this finding concerns, if the finding is per-employee (null for run-level).</summary>
    public Guid? EmployeeId { get; set; }
}
