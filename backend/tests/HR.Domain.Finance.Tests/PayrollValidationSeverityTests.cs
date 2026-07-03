using FluentAssertions;
using HR.Domain.Engines.Finance;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>TDD tests for Task 11: 3-severity validation findings (only Error blocks approval)
/// with deep-link metadata on ValidationFinding.</summary>
public class PayrollValidationSeverityTests
{
    [Fact]
    public void Warnings_do_not_block_validity()
    {
        var report = new ValidationReport(new[] {
            new ValidationFinding("MISSING_PAYMENT_METHOD", ValidationSeverity.Warning, "No payment method",
                "Set a payment method", "Employees", "employee-profile", "Employee", Guid.NewGuid(), Guid.NewGuid()) });
        report.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Errors_block_validity()
    {
        var report = new ValidationReport(new[] {
            new ValidationFinding("NEGATIVE_NET", ValidationSeverity.Error, "Net < 0",
                "Review deductions", "Payroll", "run", "Employee", Guid.NewGuid(), Guid.NewGuid()) });
        report.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Information_findings_do_not_block_validity()
    {
        var report = new ValidationReport(new[] {
            new ValidationFinding("INFO_NOTE", ValidationSeverity.Information, "FYI note",
                null, null, null, null, null, null) });
        report.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Mixed_warning_and_error_blocks_validity()
    {
        var report = new ValidationReport(new[]
        {
            new ValidationFinding("MISSING_ATTENDANCE", ValidationSeverity.Warning, "No attendance",
                null, null, null, null, null, null),
            new ValidationFinding("NEGATIVE_SALARY", ValidationSeverity.Error, "Negative salary",
                null, null, null, null, null, null),
        });
        report.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Finding_stores_deep_link_fields()
    {
        var relatedEntityId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var f = new ValidationFinding(
            "NEGATIVE_SALARY",
            ValidationSeverity.Error,
            "Employee E1 has a negative salary.",
            "Review the salary configuration.",
            "Payroll",
            "run",
            "Employee",
            relatedEntityId,   // positional arg 8 → RelatedEntityId
            employeeId);       // positional arg 9 → EmployeeId

        f.Code.Should().Be("NEGATIVE_SALARY");
        f.Severity.Should().Be(ValidationSeverity.Error);
        f.SuggestedAction.Should().Be("Review the salary configuration.");
        f.TargetModule.Should().Be("Payroll");
        f.TargetScreen.Should().Be("run");
        f.RelatedEntityType.Should().Be("Employee");
        f.RelatedEntityId.Should().Be(relatedEntityId);
        f.EmployeeId.Should().Be(employeeId);
    }
}
