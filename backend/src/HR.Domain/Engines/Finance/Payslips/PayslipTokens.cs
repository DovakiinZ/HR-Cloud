using System.Globalization;

namespace HR.Domain.Engines.Finance.Payslips;

/// <summary>The primitive inputs a payslip document needs, assembled by the payslip service from the frozen
/// PayrollPayslip snapshot, its run (period/number/pay date), the employee record, and the company profile.
/// Kept as a flat context so the token mapping stays pure and unit-testable without constructing entities.</summary>
public sealed record PayslipTokenContext(
    string EmployeeName, string EmployeeNumber, string? Department, string? Position,
    string? NationalId, string? Iban, string? PaymentMethod,
    int PeriodYear, int PeriodMonth, string RunNumber, System.DateTime? PayDate,
    decimal GrossEarnings, decimal TotalDeductions, decimal NetAmount, string Currency,
    System.DateTime GeneratedAt, string? GeneratedBy,
    string? CompanyNameAr, string? CompanyNameEn, string? CompanyCr, string? CompanyVat,
    string? CompanyPhone, string? CompanyEmail, string? CompanyWebsite, string? CompanyAddress);

/// <summary>Pure mapping of a <see cref="PayslipTokenContext"/> into the Company.* / Employee.* / Payroll.*
/// scalar token dictionary the Document renderer resolves. Missing optional values render as an em dash so
/// templates never show empty gaps. Money is formatted with invariant grouping; the currency is a separate
/// token so templates place it where they like.</summary>
public static class PayslipTokens
{
    private const string Dash = "—";

    public static Dictionary<string, string> Build(PayslipTokenContext c)
    {
        static string Or(string? v) => string.IsNullOrWhiteSpace(v) ? Dash : v!;
        static string Money(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);

        return new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["Company.NameAr"] = Or(c.CompanyNameAr),
            ["Company.NameEn"] = Or(c.CompanyNameEn),
            ["Company.CR"] = Or(c.CompanyCr),
            ["Company.VAT"] = Or(c.CompanyVat),
            ["Company.Phone"] = Or(c.CompanyPhone),
            ["Company.Email"] = Or(c.CompanyEmail),
            ["Company.Website"] = Or(c.CompanyWebsite),
            ["Company.Address"] = Or(c.CompanyAddress),

            ["Employee.Name"] = Or(c.EmployeeName),
            ["Employee.Number"] = Or(c.EmployeeNumber),
            ["Employee.Department"] = Or(c.Department),
            ["Employee.Position"] = Or(c.Position),
            ["Employee.NationalId"] = Or(c.NationalId),
            ["Employee.IBAN"] = Or(c.Iban),
            ["Employee.PaymentMethod"] = Or(c.PaymentMethod),

            ["Payroll.Period"] = $"{c.PeriodYear:D4}-{c.PeriodMonth:D2}",
            ["Payroll.PayDate"] = c.PayDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? Dash,
            ["Payroll.RunNumber"] = Or(c.RunNumber),
            ["Payroll.GrossSalary"] = Money(c.GrossEarnings),
            ["Payroll.TotalEarnings"] = Money(c.GrossEarnings),
            ["Payroll.TotalDeductions"] = Money(c.TotalDeductions),
            ["Payroll.NetSalary"] = Money(c.NetAmount),
            ["Payroll.Currency"] = Or(c.Currency),
            ["Payroll.GeneratedAt"] = c.GeneratedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            ["Payroll.GeneratedBy"] = Or(c.GeneratedBy),
        };
    }
}
