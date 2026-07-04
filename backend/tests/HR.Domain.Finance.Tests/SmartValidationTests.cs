using FluentAssertions;
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance;
using HR.Domain.Enums;
using HR.Infrastructure.Engines.Finance.Validators;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>SP9 — smart validation extending the existing IPayrollValidator framework with duplicate,
/// conflict and consistency checks that surface as run findings.</summary>
public class SmartValidationTests
{
    private static ComponentResult Comp(string code, PayComponentKind kind, decimal amount)
        => new(code, code, kind, amount, true);

    private static EmployeePayrollResult Result(string number, string name, decimal gross, decimal deductions, params ComponentResult[] comps)
    {
        var input = new EmployeePayrollInput { EmployeeId = System.Guid.NewGuid(), EmployeeNumber = number, EmployeeName = name };
        var eval = new RuleSetEvaluation(comps, comps.Select(c => c.Code).ToList(), gross, deductions, gross - deductions);
        return new EmployeePayrollResult { Input = input, Evaluation = eval };
    }

    private static PayrollValidationContext Ctx(params EmployeePayrollResult[] results) => new()
    {
        Period = PayrollPeriod.Monthly(2026, 7),
        Currency = "SAR",
        Results = results,
        Inputs = results.Select(r => r.Input).ToList(),
    };

    // ── Duplicate transactions ────────────────────────────────────────────────────

    [Fact]
    public void DuplicateTransaction_warns_on_same_type_and_amount_twice()
    {
        var ctx = Ctx(Result("E1", "Ali", 5000m, 1000m,
            Comp("BASIC", PayComponentKind.Earning, 5000m),
            Comp("LOAN", PayComponentKind.Deduction, 500m),
            Comp("LOAN", PayComponentKind.Deduction, 500m)));

        var findings = new DuplicateTransactionValidator().Validate(ctx).ToList();

        findings.Should().ContainSingle(f => f.Code == "DUPLICATE_TRANSACTION" && f.EmployeeName == "Ali");
        findings[0].Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public void DuplicateTransaction_ignores_same_type_different_amount()
    {
        var ctx = Ctx(Result("E1", "Ali", 0m, 800m,
            Comp("LOAN", PayComponentKind.Deduction, 500m),
            Comp("LOAN", PayComponentKind.Deduction, 300m)));

        new DuplicateTransactionValidator().Validate(ctx).Should().BeEmpty();
    }

    // ── Excessive deductions (conflict, but net still ≥ 0 so NegativeSalary doesn't fire) ──

    [Fact]
    public void ExcessiveDeduction_warns_when_deductions_exceed_90pct_of_gross()
    {
        var ctx = Ctx(Result("E1", "Ali", 5000m, 4800m,
            Comp("BASIC", PayComponentKind.Earning, 5000m),
            Comp("X", PayComponentKind.Deduction, 4800m)));

        new ExcessiveDeductionValidator().Validate(ctx)
            .Should().ContainSingle(f => f.Code == "EXCESSIVE_DEDUCTION" && f.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void ExcessiveDeduction_silent_for_modest_deductions()
    {
        var ctx = Ctx(Result("E1", "Ali", 5000m, 500m,
            Comp("BASIC", PayComponentKind.Earning, 5000m),
            Comp("X", PayComponentKind.Deduction, 500m)));

        new ExcessiveDeductionValidator().Validate(ctx).Should().BeEmpty();
    }

    // ── Zero gross (consistency: missing salary structure) ────────────────────────

    [Fact]
    public void ZeroGross_warns_for_included_employee_with_no_earnings()
    {
        new ZeroGrossValidator().Validate(Ctx(Result("E1", "Ali", 0m, 0m)))
            .Should().ContainSingle(f => f.Code == "ZERO_GROSS");
    }

    [Fact]
    public void ZeroGross_silent_with_positive_gross()
    {
        var ctx = Ctx(Result("E1", "Ali", 5000m, 0m, Comp("BASIC", PayComponentKind.Earning, 5000m)));
        new ZeroGrossValidator().Validate(ctx).Should().BeEmpty();
    }
}
