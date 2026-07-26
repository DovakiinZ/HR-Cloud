using HR.Domain.Engines.MasterData;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Documents;

/// <summary>
/// Builds the token → value dictionary for a request instance (Employee / Request / Leave /
/// Payroll / Company / System). Shared by the PDF renderer and any preview path so the same
/// values resolve everywhere. Values are returned under canonical dotted keys plus legacy aliases.
/// </summary>
public interface IDocumentTokenResolver
{
    Task<Dictionary<string, string>> ResolveForRequestAsync(Guid requestInstanceId, CancellationToken ct);
}

public sealed class DocumentTokenResolver : IDocumentTokenResolver, IRequestTokenResolver
{
    private readonly ApplicationDbContext _db;
    public DocumentTokenResolver(ApplicationDbContext db) => _db = db;

    private static string Money(decimal v) => v.ToString("#,##0.##");
    private static string D(DateTime? d) => d?.ToString("yyyy-MM-dd") ?? "";

    public async Task<Dictionary<string, string>> ResolveForRequestAsync(Guid requestInstanceId, CancellationToken ct)
    {
        var instance = await _db.RequestInstances.Include(r => r.RequestType)
            .FirstOrDefaultAsync(r => r.Id == requestInstanceId, ct)
            ?? throw new HR.Application.Common.Exceptions.NotFoundException("RequestInstance", requestInstanceId);

        var employee = await _db.Employees.Include(e => e.Allowances)
            .FirstOrDefaultAsync(e => e.Id == instance.EmployeeId, ct);
        var company = await _db.CompanyProfiles.FirstOrDefaultAsync(ct);

        var department = employee?.DepartmentId is { } depId
            ? await _db.Departments.Where(d => d.Id == depId).Select(d => d.NameAr).FirstOrDefaultAsync(ct) : null;
        var jobTitle = employee?.JobTitleId is { } jtId
            ? await _db.MasterDataItems.Where(m => m.Id == jtId).Select(m => m.NameAr).FirstOrDefaultAsync(ct) : null;
        var nationality = employee?.NationalityId is { } natId
            ? await _db.MasterDataItems.Where(m => m.Id == natId).Select(m => m.NameAr).FirstOrDefaultAsync(ct) : null;
        var leaveType = instance.LeaveTypeId is { } ltId
            ? await _db.MasterDataItems.Where(m => m.Id == ltId).Select(m => m.NameAr).FirstOrDefaultAsync(ct) : null;
        var managerName = employee?.ManagerId is { } mId
            ? await _db.Employees.Where(e => e.Id == mId).Select(e => (e.FirstNameAr ?? e.FirstName) + " " + (e.LastNameAr ?? e.LastName)).FirstOrDefaultAsync(ct) : null;

        // Payroll: basic + allowances (housing/transport matched by allowance-type name).
        decimal basic = employee?.BasicSalary ?? 0m;
        decimal housing = 0m, transport = 0m, allowancesTotal = 0m;
        if (employee is not null && employee.Allowances.Count > 0)
        {
            var typeIds = employee.Allowances.Select(a => a.AllowanceTypeId).Distinct().ToList();
            var names = await _db.MasterDataItems.Where(m => typeIds.Contains(m.Id))
                .Select(m => new { m.Id, m.NameAr, m.NameEn }).ToListAsync(ct);
            foreach (var a in employee.Allowances.Where(a => a.IsActive))
            {
                allowancesTotal += a.Amount;
                var n = names.FirstOrDefault(x => x.Id == a.AllowanceTypeId);
                var label = $"{n?.NameAr} {n?.NameEn}".ToLowerInvariant();
                if (label.Contains("سكن") || label.Contains("housing")) housing += a.Amount;
                else if (label.Contains("نقل") || label.Contains("مواصلات") || label.Contains("transport")) transport += a.Amount;
            }
        }
        decimal total = basic + allowancesTotal;

        var employeeName = employee is null ? "—" : $"{employee.FirstNameAr ?? employee.FirstName} {employee.LastNameAr ?? employee.LastName}".Trim();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var t = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Employee.FullName"] = employeeName,
            ["Employee.EmployeeNumber"] = employee?.EmployeeNumber ?? "",
            ["Employee.Department"] = department ?? "",
            ["Employee.JobTitle"] = jobTitle ?? "",
            ["Employee.Manager"] = managerName ?? "",
            ["Employee.Nationality"] = nationality ?? "",
            ["Employee.NationalId"] = employee?.NationalId ?? "",
            ["Employee.HireDate"] = employee is null ? "" : D(employee.HireDate),
            ["Employee.Email"] = employee?.Email ?? "",
            ["Employee.Phone"] = employee?.Phone ?? "",

            ["Request.Number"] = instance.RequestNumber,
            ["Request.Type"] = instance.RequestType.NameAr,
            ["Request.CreatedDate"] = D(instance.SubmittedAt),
            ["Request.ApprovalDate"] = D(instance.DecidedAt),
            ["Request.Status"] = StatusAr(instance.Status.ToString()),

            ["Leave.Type"] = leaveType ?? "",
            ["Leave.StartDate"] = D(instance.StartDate),
            ["Leave.EndDate"] = D(instance.EndDate),
            ["Leave.Days"] = instance.DaysCount?.ToString() ?? "",

            ["Payroll.BasicSalary"] = Money(basic),
            ["Payroll.HousingAllowance"] = Money(housing),
            ["Payroll.TransportationAllowance"] = Money(transport),
            ["Payroll.TotalSalary"] = Money(total),
            ["Payroll.Currency"] = employee?.Currency ?? "SAR",

            ["Company.Name"] = company?.NameAr ?? "",
            ["Company.NameEn"] = company?.NameEn ?? "",
            ["Company.CR"] = company?.CommercialRegistration ?? "",
            ["Company.VAT"] = company?.VatNumber ?? "",
            ["Company.Address"] = string.Join(" ", new[] { company?.Address, company?.City, company?.Country }.Where(s => !string.IsNullOrWhiteSpace(s))),
            ["Company.Phone"] = company?.Phone ?? "",
            ["Company.Email"] = company?.Email ?? "",
            ["Company.Website"] = company?.Website ?? "",

            ["System.Today"] = today,

            // Legacy aliases (originally-seeded token names)
            ["Request.LeaveType"] = leaveType ?? "",
            ["Request.StartDate"] = D(instance.StartDate),
            ["Request.EndDate"] = D(instance.EndDate),
            ["Request.Days"] = instance.DaysCount?.ToString() ?? "",
            ["EmployeeName"] = employeeName,
            ["EmployeeNumber"] = employee?.EmployeeNumber ?? "",
            ["Department"] = department ?? "",
            ["JobTitle"] = jobTitle ?? "",
            ["LeaveType"] = leaveType ?? "",
            ["StartDate"] = D(instance.StartDate),
            ["EndDate"] = D(instance.EndDate),
            ["CompanyName"] = company?.NameAr ?? "",
            ["CRNumber"] = company?.CommercialRegistration ?? "",
            ["VATNumber"] = company?.VatNumber ?? "",
            ["GeneratedDate"] = today,
        };
        return t;
    }

    private static string StatusAr(string status) => status switch
    {
        "Approved" => "معتمد", "Rejected" => "مرفوض", "Submitted" => "مُقدّم", "InProgress" => "قيد المعالجة",
        "Returned" => "مُعاد", "Cancelled" => "ملغي", "Pending" => "بانتظار", _ => status,
    };

    // Explicit implementation of the thin IRequestTokenResolver seam used by the dispatcher.
    async Task<IReadOnlyDictionary<string, string>> IRequestTokenResolver.ResolveForRequestAsync(
        Guid requestInstanceId, CancellationToken ct)
        => await ResolveForRequestAsync(requestInstanceId, ct);
}
