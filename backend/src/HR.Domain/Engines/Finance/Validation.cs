namespace HR.Domain.Engines.Finance;

public enum ValidationSeverity
{
    Warning = 1,
    Error = 2,
    Information = 3,
}

/// <summary>One finding produced by a payroll validator: a stable machine code, a severity, a human
/// message, deep-link metadata, and the employee it concerns (null for run-level findings).
/// Only <see cref="ValidationSeverity.Error"/> findings block payroll approval.</summary>
public sealed record ValidationFinding(
    string Code,
    ValidationSeverity Severity,
    string Message,
    string? SuggestedAction = null,
    string? TargetModule = null,
    string? TargetScreen = null,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null,
    Guid? EmployeeId = null,
    string? EmployeeName = null)
{
    public static ValidationFinding Error(
        string code, string message,
        string? suggestedAction = null,
        string? targetModule = null, string? targetScreen = null,
        string? relatedEntityType = null, Guid? relatedEntityId = null,
        Guid? employeeId = null, string? employeeName = null)
        => new(code, ValidationSeverity.Error, message,
            suggestedAction, targetModule, targetScreen, relatedEntityType, relatedEntityId,
            employeeId, employeeName);

    public static ValidationFinding Warning(
        string code, string message,
        string? suggestedAction = null,
        string? targetModule = null, string? targetScreen = null,
        string? relatedEntityType = null, Guid? relatedEntityId = null,
        Guid? employeeId = null, string? employeeName = null)
        => new(code, ValidationSeverity.Warning, message,
            suggestedAction, targetModule, targetScreen, relatedEntityType, relatedEntityId,
            employeeId, employeeName);
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

    /// <summary>True when no <see cref="ValidationSeverity.Error"/> findings exist.
    /// Warnings and Information findings do NOT block payroll approval.</summary>
    public bool IsValid => !Findings.Any(f => f.Severity == ValidationSeverity.Error);

    public static ValidationReport From(IEnumerable<ValidationFinding> findings) => new(findings.ToList());
}
