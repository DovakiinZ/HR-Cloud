namespace HR.Domain.Engines.Finance;

public enum ValidationSeverity
{
    Warning = 1,
    Error = 2,
    Information = 3,
}

/// <summary>One finding produced by a payroll validator: a stable machine code, a severity, a human
/// message, and the employee it concerns (null for run-level findings).</summary>
public sealed record ValidationFinding(
    string Code,
    ValidationSeverity Severity,
    string Message,
    Guid? EmployeeId = null,
    string? EmployeeName = null)
{
    public static ValidationFinding Error(string code, string message, Guid? employeeId = null, string? employeeName = null)
        => new(code, ValidationSeverity.Error, message, employeeId, employeeName);

    public static ValidationFinding Warning(string code, string message, Guid? employeeId = null, string? employeeName = null)
        => new(code, ValidationSeverity.Warning, message, employeeId, employeeName);
}

/// <summary>The aggregated result of validating a payroll run. A run cannot be executed while any
/// <see cref="ValidationSeverity.Error"/> finding exists.</summary>
public sealed record ValidationReport(IReadOnlyList<ValidationFinding> Findings)
{
    public static ValidationReport Empty { get; } = new(Array.Empty<ValidationFinding>());

    public IReadOnlyList<ValidationFinding> Errors =>
        Findings.Where(f => f.Severity == ValidationSeverity.Error).ToList();

    public IReadOnlyList<ValidationFinding> Warnings =>
        Findings.Where(f => f.Severity == ValidationSeverity.Warning).ToList();

    public bool IsValid => Findings.All(f => f.Severity != ValidationSeverity.Error);

    public static ValidationReport From(IEnumerable<ValidationFinding> findings) => new(findings.ToList());
}
