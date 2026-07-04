using FluentAssertions;
using HR.Domain.Engines.Finance.Payslips;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>SP4 Task 4 — pure mapping of a payslip's identity/period/totals + company profile into the
/// Company.* / Employee.* / Payroll.* token dictionary the Document renderer resolves.</summary>
public class PayslipTokensTests
{
    private static PayslipTokenContext Full() => new(
        EmployeeName: "Sara Ali", EmployeeNumber: "E001", Department: "HR", Position: "Officer",
        NationalId: "1234567890", Iban: "SA0000", PaymentMethod: "Bank Transfer",
        PeriodYear: 2026, PeriodMonth: 7, RunNumber: "PR-2026-00003", PayDate: new System.DateTime(2026, 7, 27),
        GrossEarnings: 6250m, TotalDeductions: 500m, NetAmount: 5750m, Currency: "SAR",
        GeneratedAt: new System.DateTime(2026, 7, 28, 10, 0, 0), GeneratedBy: "admin",
        CompanyNameAr: "شركة سند", CompanyNameEn: "Sanad Co", CompanyCr: "CR-1", CompanyVat: "VAT-1",
        CompanyPhone: "011", CompanyEmail: "a@b.c", CompanyWebsite: "sanad.sa", CompanyAddress: "Riyadh");

    [Fact]
    public void Build_maps_company_employee_and_payroll_tokens()
    {
        var t = PayslipTokens.Build(Full());

        t["Company.NameAr"].Should().Be("شركة سند");
        t["Company.NameEn"].Should().Be("Sanad Co");
        t["Company.CR"].Should().Be("CR-1");
        t["Employee.Name"].Should().Be("Sara Ali");
        t["Employee.Number"].Should().Be("E001");
        t["Employee.IBAN"].Should().Be("SA0000");
        t["Payroll.Period"].Should().Be("2026-07");
        t["Payroll.RunNumber"].Should().Be("PR-2026-00003");
        t["Payroll.GrossSalary"].Should().Be("6,250.00");
        t["Payroll.TotalDeductions"].Should().Be("500.00");
        t["Payroll.NetSalary"].Should().Be("5,750.00");
        t["Payroll.Currency"].Should().Be("SAR");
    }

    [Fact]
    public void Build_uses_dash_for_missing_optional_values()
    {
        var ctx = Full() with { Department = null, Iban = null };
        var t = PayslipTokens.Build(ctx);
        t["Employee.Department"].Should().Be("—");
        t["Employee.IBAN"].Should().Be("—");
    }
}
