using HR.Application.Engines.Documents;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Documents;

/// <summary>Builds a branded expense PDF by funnelling through the shared <see cref="IDocumentRenderer"/> —
/// same look-and-feel as the payslip.</summary>
public sealed class ExpenseDocumentService : IExpenseDocumentService
{
    private readonly ApplicationDbContext _db;
    private readonly IDocumentRenderer _renderer;

    public ExpenseDocumentService(ApplicationDbContext db, IDocumentRenderer renderer) { _db = db; _renderer = renderer; }

    public async Task<RenderedDocument> RenderAsync(Guid expenseId, CancellationToken ct = default)
    {
        var exp = await _db.Expenses.AsNoTracking().FirstOrDefaultAsync(e => e.Id == expenseId, ct)
            ?? throw new InvalidOperationException("Expense not found");

        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == exp.EmployeeId, ct);
        var catName = exp.ExpenseCategoryId is { } cid
            ? await _db.MasterDataItems.AsNoTracking().Where(m => m.Id == cid).Select(m => m.NameAr).FirstOrDefaultAsync(ct)
            : null;

        var name = emp != null ? $"{emp.FirstNameAr ?? emp.FirstName} {emp.LastNameAr ?? emp.LastName}".Trim() : "";
        var number = emp?.EmployeeNumber ?? "";
        string m(decimal v) => $"{v:N2} {exp.Currency}";
        var statusAr = exp.Status switch
        {
            "Approved" => "معتمد", "Paid" => "مدفوع", "Rejected" => "مرفوض",
            "Cancelled" => "ملغى", "Pending" => "قيد الاعتماد", _ => exp.Status,
        };

        var details = new List<(string, string)>
        {
            ("الموظف", name),
            ("الرقم الوظيفي", number),
            ("الفئة", catName ?? "—"),
            ("المبلغ", m(exp.Amount)),
            ("الوصف", string.IsNullOrWhiteSpace(exp.Description) ? "—" : exp.Description!),
            ("التاريخ", exp.DecidedAt.ToString("yyyy-MM-dd")),
            ("الحالة", statusAr),
        };
        if (exp.IncludeInPayroll && exp.PayrollMonth is { } pm)
            details.Add(("شهر التضمين في الرواتب", pm.ToString("yyyy-MM")));

        var tokens = new Dictionary<string, string>
        {
            ["employee"] = name, ["employeeNumber"] = number, ["total"] = m(exp.Amount),
        };

        var fileName = $"expense-{number}-{exp.Id:N}.pdf";
        var req = new DocumentRenderRequest(
            TemplateId: null,
            FallbackTitle: "سند مصروف",
            RefNumber: number,
            Tokens: tokens,
            DefaultDetails: details,
            Approvals: null,
            FileName: fileName);

        var (pdf, _) = await _renderer.RenderDocumentAsync(req, ct);
        return new RenderedDocument(pdf, fileName);
    }
}
