using FluentAssertions;
using HR.Domain.Enums;
using HR.Infrastructure.Engines.Finance;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class PayrollValidityTests
{
    [Theory]
    [InlineData("2026-08-01", null, PayrollExclusionReasonCode.NotEmployedInPeriod)] // hired after period
    [InlineData("2020-01-01", "2026-06-30", PayrollExclusionReasonCode.NotEmployedInPeriod)] // left before period
    public void Not_employed_in_period_is_excluded(string hire, string? term, PayrollExclusionReasonCode expected)
    {
        var v = PayrollValidityEvaluator.Evaluate(
            hireDate: DateTime.Parse(hire), terminationDate: term is null ? null : DateTime.Parse(term),
            basicSalary: 5000m, periodStart: new DateTime(2026, 7, 1), periodEnd: new DateTime(2026, 7, 31));
        v.Should().Be(expected);
    }

    [Fact]
    public void No_salary_is_excluded()
        => PayrollValidityEvaluator.Evaluate(new DateTime(2020, 1, 1), null, 0m,
            new DateTime(2026, 7, 1), new DateTime(2026, 7, 31)).Should().Be(PayrollExclusionReasonCode.NoActiveSalary);

    [Fact]
    public void Employed_with_salary_is_valid()
        => PayrollValidityEvaluator.Evaluate(new DateTime(2020, 1, 1), null, 5000m,
            new DateTime(2026, 7, 1), new DateTime(2026, 7, 31)).Should().BeNull();

    // --- Boundary cases ---

    [Fact]
    public void Hired_on_period_start_day_is_included()
        => PayrollValidityEvaluator.Evaluate(new DateTime(2026, 7, 1), null, 5000m,
            new DateTime(2026, 7, 1), new DateTime(2026, 7, 31)).Should().BeNull();

    [Fact]
    public void Hired_on_period_end_day_is_included()
        => PayrollValidityEvaluator.Evaluate(new DateTime(2026, 7, 31), null, 5000m,
            new DateTime(2026, 7, 1), new DateTime(2026, 7, 31)).Should().BeNull();

    [Fact]
    public void Terminated_on_period_start_day_is_included()
        => PayrollValidityEvaluator.Evaluate(new DateTime(2020, 1, 1), new DateTime(2026, 7, 1), 5000m,
            new DateTime(2026, 7, 1), new DateTime(2026, 7, 31)).Should().BeNull();

    [Fact]
    public void Terminated_on_period_end_day_is_included()
        => PayrollValidityEvaluator.Evaluate(new DateTime(2020, 1, 1), new DateTime(2026, 7, 31), 5000m,
            new DateTime(2026, 7, 1), new DateTime(2026, 7, 31)).Should().BeNull();

    [Fact]
    public void Negative_salary_is_excluded_as_no_active_salary()
        => PayrollValidityEvaluator.Evaluate(new DateTime(2020, 1, 1), null, -100m,
            new DateTime(2026, 7, 1), new DateTime(2026, 7, 31)).Should().Be(PayrollExclusionReasonCode.NoActiveSalary);
}
