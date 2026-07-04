using HR.Application.Engines.Finance.Export;
using HR.Domain.Engines.Finance.Payslips;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Payroll.Export;

/// <summary>Per-employee payroll register: gross / deductions / net for the run.</summary>
public sealed class RunSummaryReportProvider : IPayrollReportProvider
{
    private readonly ApplicationDbContext _db;
    public RunSummaryReportProvider(ApplicationDbContext db) => _db = db;

    public string Kind => "RunSummary";
    public string Title => "ملخص المسيّر";

    public async Task<TabularDataset> BuildAsync(Guid runId, CancellationToken ct)
    {
        var rows = await _db.PayrollPayslips.AsNoTracking()
            .Where(p => p.PayrollRunId == runId)
            .OrderBy(p => p.EmployeeName)
            .Select(p => new { p.EmployeeNumber, p.EmployeeName, p.GrossEarnings, p.TotalDeductions, p.NetAmount, p.Currency })
            .ToListAsync(ct);

        var columns = new[]
        {
            new TabularColumn("num", "الرقم الوظيفي"),
            new TabularColumn("name", "الموظف"),
            new TabularColumn("gross", "الإجمالي"),
            new TabularColumn("deductions", "الاستقطاعات"),
            new TabularColumn("net", "الصافي"),
            new TabularColumn("currency", "العملة"),
        };

        var data = rows.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["num"] = r.EmployeeNumber,
            ["name"] = r.EmployeeName,
            ["gross"] = r.GrossEarnings,
            ["deductions"] = r.TotalDeductions,
            ["net"] = r.NetAmount,
            ["currency"] = r.Currency,
        }).ToList();

        return new TabularDataset("RunSummary", columns, data);
    }
}

/// <summary>Long-format component breakdown: one row per (employee, applied component) from the frozen
/// ComponentsJson — the "no hidden line items" detail report.</summary>
public sealed class EmployeeDetailReportProvider : IPayrollReportProvider
{
    private readonly ApplicationDbContext _db;
    public EmployeeDetailReportProvider(ApplicationDbContext db) => _db = db;

    public string Kind => "EmployeeDetail";
    public string Title => "تفصيل مكونات الرواتب";

    public async Task<TabularDataset> BuildAsync(Guid runId, CancellationToken ct)
    {
        var payslips = await _db.PayrollPayslips.AsNoTracking()
            .Where(p => p.PayrollRunId == runId)
            .OrderBy(p => p.EmployeeName)
            .Select(p => new { p.EmployeeNumber, p.EmployeeName, p.Currency, p.ComponentsJson })
            .ToListAsync(ct);

        var columns = new[]
        {
            new TabularColumn("num", "الرقم الوظيفي"),
            new TabularColumn("name", "الموظف"),
            new TabularColumn("type", "النوع"),
            new TabularColumn("component", "المكوّن"),
            new TabularColumn("amount", "المبلغ"),
            new TabularColumn("currency", "العملة"),
        };

        var data = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var p in payslips)
        {
            var b = PayslipComponentProjection.Project(p.ComponentsJson);
            foreach (var e in b.Earnings)
                data.Add(Row(p.EmployeeNumber, p.EmployeeName, "إيراد", e.ComponentCode, e.Amount, p.Currency));
            foreach (var d in b.Deductions)
                data.Add(Row(p.EmployeeNumber, p.EmployeeName, "استقطاع", d.ComponentCode, d.Amount, p.Currency));
        }

        return new TabularDataset("EmployeeDetail", columns, data);
    }

    private static IReadOnlyDictionary<string, object?> Row(string num, string name, string type, string component, decimal amount, string currency)
        => new Dictionary<string, object?>
        {
            ["num"] = num, ["name"] = name, ["type"] = type,
            ["component"] = component, ["amount"] = amount, ["currency"] = currency,
        };
}
