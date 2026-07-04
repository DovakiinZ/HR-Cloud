using HR.Application.Engines.Documents;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Documents;

/// <summary>Builds a branded loan/advance PDF (employee, terms, and the full installment schedule) by
/// funnelling through the shared <see cref="IDocumentRenderer"/> — same look-and-feel as the payslip.</summary>
public sealed class LoanDocumentService : ILoanDocumentService
{
    private readonly ApplicationDbContext _db;
    private readonly IDocumentRenderer _renderer;

    public LoanDocumentService(ApplicationDbContext db, IDocumentRenderer renderer) { _db = db; _renderer = renderer; }

    public async Task<RenderedDocument> RenderAsync(Guid loanId, CancellationToken ct = default)
    {
        var loan = await _db.Loans.AsNoTracking().Include(l => l.Installments)
            .FirstOrDefaultAsync(l => l.Id == loanId, ct)
            ?? throw new InvalidOperationException("Loan not found");

        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == loan.EmployeeId, ct);
        var typeName = loan.LoanTypeId is { } tid
            ? await _db.MasterDataItems.AsNoTracking().Where(m => m.Id == tid).Select(m => m.NameAr).FirstOrDefaultAsync(ct)
            : null;

        var name = emp != null ? $"{emp.FirstNameAr ?? emp.FirstName} {emp.LastNameAr ?? emp.LastName}".Trim() : "";
        var number = emp?.EmployeeNumber ?? "";
        string m(decimal v) => $"{v:N2} ريال";
        var kindAr = loan.Kind == "Advance" ? "سلفة" : "قرض";
        var statusAr = loan.Status switch
        {
            "Active" => "نشط", "Settled" => "مُسوّاة", "Cancelled" => "ملغاة", _ => loan.Status,
        };

        var details = new List<(string, string)>
        {
            ("الموظف", name),
            ("الرقم الوظيفي", number),
            ("النوع", typeName != null ? $"{kindAr} — {typeName}" : kindAr),
            ("المبلغ الإجمالي", m(loan.Principal)),
            ("عدد الأقساط", loan.InstallmentMonths.ToString()),
            ("القسط الشهري", m(loan.MonthlyInstallment)),
            ("بداية الخصم", loan.StartDate.ToString("yyyy-MM")),
            ("الحالة", statusAr),
        };
        foreach (var i in loan.Installments.OrderBy(i => i.DueMonth))
            details.Add(($"قسط {i.DueMonth:yyyy-MM}", $"{m(i.Amount)} — {(i.Paid ? "مسدد" : "مستحق")}"));

        var tokens = new Dictionary<string, string>
        {
            ["employee"] = name, ["employeeNumber"] = number, ["total"] = m(loan.Principal),
        };

        var fileName = $"loan-{number}-{loan.Id:N}.pdf";
        var req = new DocumentRenderRequest(
            TemplateId: null,
            FallbackTitle: loan.Kind == "Advance" ? "سند سلفة" : "سند قرض",
            RefNumber: number,
            Tokens: tokens,
            DefaultDetails: details,
            Approvals: null,
            FileName: fileName);

        var (pdf, _) = await _renderer.RenderDocumentAsync(req, ct);
        return new RenderedDocument(pdf, fileName);
    }
}
