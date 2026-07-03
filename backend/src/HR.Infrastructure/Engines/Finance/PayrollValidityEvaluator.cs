using HR.Domain.Enums;

namespace HR.Infrastructure.Engines.Finance;

/// <summary>Pure, stateless per-employee validity check executed at Calculate time.
/// Returns the structural reason an employee should be excluded from a payroll period,
/// or null when the employee is valid for the period.
///
/// This evaluator is intentionally DB-free (no DbContext, no DI) so it is fully unit-testable.
/// The one reason that requires a DB lookup — <see cref="PayrollExclusionReasonCode.AlreadyInActiveRunForPeriod"/>
/// — is NOT checked here; it must be computed in the Calculate orchestration layer where
/// a DbContext is available (Task 12).</summary>
public static class PayrollValidityEvaluator
{
    /// <summary>Determines whether an employee is valid for the given payroll period.
    /// Date comparisons are done on the .Date component only (time-of-day is ignored).</summary>
    /// <param name="hireDate">The employee's hire/start date.</param>
    /// <param name="terminationDate">The employee's termination date, or null if still active.</param>
    /// <param name="basicSalary">The employee's active basic salary. Must be &gt; 0 to be considered active.</param>
    /// <param name="periodStart">The first day (inclusive) of the payroll period.</param>
    /// <param name="periodEnd">The last day (inclusive) of the payroll period.</param>
    /// <returns>
    /// <see cref="PayrollExclusionReasonCode.NotEmployedInPeriod"/> if the employee was not employed
    /// at any point within [periodStart, periodEnd];
    /// <see cref="PayrollExclusionReasonCode.NoActiveSalary"/> if the employee is employed but has
    /// no active salary (basicSalary &lt;= 0);
    /// null if the employee is valid for the period.
    /// </returns>
    public static PayrollExclusionReasonCode? Evaluate(
        DateTime hireDate,
        DateTime? terminationDate,
        decimal basicSalary,
        DateTime periodStart,
        DateTime periodEnd)
    {
        // Employee hired after the period ended → not employed during period.
        if (hireDate.Date > periodEnd.Date)
            return PayrollExclusionReasonCode.NotEmployedInPeriod;

        // Employee terminated before the period started → not employed during period.
        if (terminationDate is { } t && t.Date < periodStart.Date)
            return PayrollExclusionReasonCode.NotEmployedInPeriod;

        // Employee is employed during the period but has no active salary.
        if (basicSalary <= 0m)
            return PayrollExclusionReasonCode.NoActiveSalary;

        return null;
    }
}
