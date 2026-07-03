using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Finance.Entities;

/// <summary>Records that a specific employee was structurally excluded from a payroll calculation.
/// One row per excluded employee per <see cref="PayrollRunCalculationId"/>.
///
/// EF wiring (DbSet, FK to PayrollRunCalculation, migration) is added in Task 12, which creates
/// the PayrollRunCalculation parent entity and owns all calculation-table EF configuration.
/// This file is the POCO class only.</summary>
public class PayrollCalculationExclusion : TenantEntity
{
    /// <summary>The calculation snapshot this exclusion belongs to.
    /// FK and navigation to PayrollRunCalculation are configured in Task 12.</summary>
    public Guid PayrollRunCalculationId { get; set; }

    /// <summary>The employee who was excluded.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>Structural reason the employee was excluded from this calculation.</summary>
    public PayrollExclusionReasonCode ReasonCode { get; set; }

    /// <summary>Optional human-readable detail to complement the reason code
    /// (e.g. hire date, termination date, or the conflicting run ID).</summary>
    public string? Detail { get; set; }
}
