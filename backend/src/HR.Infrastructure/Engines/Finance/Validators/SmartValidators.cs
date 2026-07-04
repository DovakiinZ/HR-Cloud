using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance;
using HR.Domain.Enums;

namespace HR.Infrastructure.Engines.Finance.Validators;

/// <summary>SP9 — flags likely double-entered transactions: the same component type and amount applied
/// more than once to an employee. Non-blocking (two identical amounts can be legitimate) — a human confirms.</summary>
public sealed class DuplicateTransactionValidator : IPayrollValidator
{
    public string Code => "DUPLICATE_TRANSACTION";

    public IEnumerable<ValidationFinding> Validate(PayrollValidationContext ctx)
    {
        foreach (var r in ctx.Results)
        {
            var applied = r.Evaluation.Components.Where(c => c.Applied
                && c.Kind is PayComponentKind.Earning or PayComponentKind.Deduction);
            foreach (var g in applied.GroupBy(c => (c.ComponentCode, c.Amount)).Where(g => g.Count() > 1))
                yield return ValidationFinding.Warning(
                    Code,
                    $"Employee {r.Input.EmployeeNumber} has {g.Count()} identical entries of {g.Key.ComponentCode} ({g.Key.Amount}) — possible duplicate.",
                    suggestedAction: "Review the additions/deductions for a double entry and remove any duplicate.",
                    targetModule: "Payroll",
                    targetScreen: "run",
                    relatedEntityType: "Employee",
                    relatedEntityId: r.EmployeeId,
                    employeeId: r.EmployeeId,
                    employeeName: r.Input.EmployeeName);
        }
    }
}

/// <summary>SP9 — warns when deductions consume nearly all of gross (net still ≥ 0, so NegativeSalary
/// doesn't fire) — a likely configuration conflict worth a human check. Non-blocking.</summary>
public sealed class ExcessiveDeductionValidator : IPayrollValidator
{
    private const decimal Threshold = 0.90m;
    public string Code => "EXCESSIVE_DEDUCTION";

    public IEnumerable<ValidationFinding> Validate(PayrollValidationContext ctx)
    {
        foreach (var r in ctx.Results)
        {
            if (r.Gross > 0m && r.Net >= 0m && r.Deductions / r.Gross > Threshold)
                yield return ValidationFinding.Warning(
                    Code,
                    $"Employee {r.Input.EmployeeNumber} deductions ({r.Deductions}) are {r.Deductions / r.Gross:P0} of gross ({r.Gross}).",
                    suggestedAction: "Confirm the deductions are correct; they leave little or no net pay.",
                    targetModule: "Payroll",
                    targetScreen: "run",
                    relatedEntityType: "Employee",
                    relatedEntityId: r.EmployeeId,
                    employeeId: r.EmployeeId,
                    employeeName: r.Input.EmployeeName);
        }
    }
}

/// <summary>SP9 — warns when an included employee computes to zero gross earnings — usually a missing or
/// misconfigured salary structure. Non-blocking.</summary>
public sealed class ZeroGrossValidator : IPayrollValidator
{
    public string Code => "ZERO_GROSS";

    public IEnumerable<ValidationFinding> Validate(PayrollValidationContext ctx)
    {
        foreach (var r in ctx.Results)
        {
            if (r.Gross == 0m)
                yield return ValidationFinding.Warning(
                    Code,
                    $"Employee {r.Input.EmployeeNumber} has zero gross earnings — the salary structure may be missing.",
                    suggestedAction: "Check the employee's salary/allowance configuration.",
                    targetModule: "Employees",
                    targetScreen: "employee-profile",
                    relatedEntityType: "Employee",
                    relatedEntityId: r.EmployeeId,
                    employeeId: r.EmployeeId,
                    employeeName: r.Input.EmployeeName);
        }
    }
}
