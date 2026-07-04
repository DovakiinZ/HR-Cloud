using System.Linq;
using System.Text.Json;
using FluentAssertions;
using HR.Domain.Engines.Finance;
using HR.Domain.Engines.Finance.Payslips;
using HR.Domain.Enums;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>SP4 Task 2 — pure projection of a payslip's immutable ComponentsJson snapshot into grouped
/// earnings/deductions + reconciled totals. Mirrors PayslipLedgerMapper's parse rules (applied + non-zero;
/// Earning/Deduction only; Contribution/Information excluded).</summary>
public class PayslipComponentProjectionTests
{
    private static string Json(params (string code, PayComponentKind kind, decimal amount, bool applied)[] comps)
        => JsonSerializer.Serialize(new
        {
            order = comps.Select(c => c.code),
            components = comps.Select(c => new ComponentResult(c.code, c.code, c.kind, c.amount, c.applied))
        });

    [Fact]
    public void Project_groups_applied_earnings_and_deductions_with_totals()
    {
        var json = Json(
            ("BASIC", PayComponentKind.Earning, 5000m, true),
            ("HOUSING", PayComponentKind.Earning, 1250m, true),
            ("GOSI", PayComponentKind.Deduction, 500m, true),
            ("UNAPPLIED", PayComponentKind.Earning, 999m, false),
            ("GOSI_EMPLOYER", PayComponentKind.Contribution, 700m, true));

        var b = PayslipComponentProjection.Project(json);

        b.Earnings.Select(e => e.ComponentCode).Should().Equal("BASIC", "HOUSING");
        b.Deductions.Select(d => d.ComponentCode).Should().Equal("GOSI");
        b.TotalEarnings.Should().Be(6250m);
        b.TotalDeductions.Should().Be(500m);
        b.NetAmount.Should().Be(5750m);
    }

    [Fact]
    public void Project_skips_zero_amount_components()
    {
        var json = Json(("BASIC", PayComponentKind.Earning, 5000m, true),
                        ("ZERO", PayComponentKind.Deduction, 0m, true));
        var b = PayslipComponentProjection.Project(json);
        b.Deductions.Should().BeEmpty();
        b.TotalDeductions.Should().Be(0m);
    }

    [Fact]
    public void Project_preserves_execution_order_of_components()
    {
        var json = Json(("HOUSING", PayComponentKind.Earning, 1250m, true),
                        ("BASIC", PayComponentKind.Earning, 5000m, true));
        PayslipComponentProjection.Project(json).Earnings.Select(e => e.ComponentCode)
            .Should().Equal("HOUSING", "BASIC");
    }

    [Fact]
    public void Project_handles_null_and_empty_json()
    {
        PayslipComponentProjection.Project(null).Earnings.Should().BeEmpty();
        PayslipComponentProjection.Project("").NetAmount.Should().Be(0m);
    }
}
