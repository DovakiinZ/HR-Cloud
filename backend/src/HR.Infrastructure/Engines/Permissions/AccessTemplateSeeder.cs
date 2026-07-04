using HR.Application.Engines.Permissions;
using HR.Domain.Engines.Permissions;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Engines.Permissions;

/// <summary>Seeds the ready-made permission templates for the current tenant. Permission codes resolve
/// against the live Permission catalog so wildcard templates (Super Admin = all, Auditor = all *.View)
/// stay correct as the catalog grows.</summary>
public sealed class AccessTemplateSeeder : IAccessTemplateSeeder
{
    private readonly ApplicationDbContext _db;
    public AccessTemplateSeeder(ApplicationDbContext db) => _db = db;

    private sealed record TemplateDef(string NameEn, string NameAr, string Description, string[]? Codes, bool AllPermissions = false, bool AllViewOnly = false);

    private static readonly TemplateDef[] Catalog =
    {
        new("Super Admin", "مدير النظام", "Full, unrestricted access to every module.", null, AllPermissions: true),
        new("HR Manager", "مدير الموارد البشرية", "Manages employees, leaves, attendance, documents and requests.", new[]
        {
            "Employees.View","Employees.Create","Employees.Edit","Employees.Export","Employees.Terminate","Employees.ViewSettlement",
            "Leaves.View","Leaves.Create","Leaves.Edit","Leaves.Assign","Leaves.Cancel",
            "Attendance.View","Attendance.Create","Attendance.Edit","Attendance.Export",
            "Documents.View","Documents.Create","Documents.Edit",
            "Requests.View","Requests.Create","Requests.Edit","Requests.Approve","Requests.Reject",
            "Departments.View","Branches.View","Reports.View","Reports.Export","Dashboards.View",
        }),
        new("Payroll Officer", "موظف الرواتب", "Prepares and runs payroll.", new[]
        {
            "Payroll.View","Payroll.Create","Payroll.Edit","Payroll.Run","Payroll.Export","Payroll.Export.Bank","Payroll.Configure",
            "Payroll.Payslip.View","Payroll.Payslip.Print","Payroll.Payslip.Download","Payroll.Run.Reissue",
            "Employees.View","Expenses.View","Loans.View","Reports.View","Dashboards.View",
        }),
        new("Finance", "المالية", "Approves and locks payroll, manages expenses and loans.", new[]
        {
            "Payroll.View","Payroll.Approve","Payroll.Lock","Payroll.Export","Payroll.Export.Bank",
            "Payroll.Payslip.View","Payroll.Payslip.Print","Payroll.Payslip.Download",
            "Payroll.Run.Void","Payroll.Run.Amend","Payroll.Run.Reissue",
            "Expenses.View","Expenses.Approve","Loans.View","Loans.Approve",
            "Reports.View","Reports.Export","Dashboards.View",
        }),
        new("Department Manager", "مدير القسم", "Approves requests and views their team.", new[]
        {
            "Employees.View","Leaves.View","Leaves.Assign","Leaves.Cancel","Attendance.View",
            "Requests.View","Requests.Approve","Requests.Reject","Tasks.View","Tasks.Create","Tasks.Assign",
            "Reports.View","Dashboards.View",
        }),
        new("Employee Self-Service", "الخدمة الذاتية للموظف", "Personal self-service access only.", new[]
        {
            "ESS.View","ESS.Create","ESS.Edit","Requests.View","Requests.Create","Leaves.View","Attendance.View","Dashboards.View",
        }),
        new("Read Only Auditor", "مدقق (قراءة فقط)", "Read-only visibility across modules plus the audit log.", null, AllViewOnly: true),
    };

    public async Task<int> EnsureDefaultsAsync(CancellationToken ct = default)
    {
        var allCodes = await _db.Permissions.Select(p => p.Module + "." + p.Name).ToListAsync(ct);
        var viewCodes = allCodes.Where(c => c.EndsWith(".View", StringComparison.OrdinalIgnoreCase)).ToList();

        var existing = await _db.PermissionTemplates.Select(t => t.NameEn).ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var created = 0;
        foreach (var def in Catalog)
        {
            if (existingSet.Contains(def.NameEn)) continue;

            var codes = def.AllPermissions ? allCodes
                : def.AllViewOnly ? viewCodes.Concat(new[] { "Settings.ViewAudit", "Reports.Export" }).Distinct().ToList()
                : (def.Codes ?? Array.Empty<string>()).Where(c => allCodes.Contains(c, StringComparer.OrdinalIgnoreCase)).Distinct().ToList();

            var template = new PermissionTemplate
            {
                NameEn = def.NameEn,
                NameAr = def.NameAr,
                Description = def.Description,
                IsSystem = true,
            };
            _db.PermissionTemplates.Add(template);
            foreach (var code in codes)
                _db.PermissionTemplateItems.Add(new PermissionTemplateItem
                {
                    PermissionTemplateId = template.Id,
                    PermissionCode = code,
                    Scope = ScopeType.Company,
                });
            created++;
        }

        if (created > 0) await _db.SaveChangesAsync(ct);
        return created;
    }
}
