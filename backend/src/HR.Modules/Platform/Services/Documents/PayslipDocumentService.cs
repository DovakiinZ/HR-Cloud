using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Documents;
using HR.Domain.Engines.Files;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.Finance.Payslips;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Documents;

/// <summary>Renders a PayrollPayslip snapshot through the Document Template engine and (optionally) archives
/// the PDF bytes as an immutable <see cref="StoredFile"/> + <see cref="GeneratedDocument"/>. Archived bytes
/// are the reproducibility guarantee: editing the DOC_PAYSLIP template later cannot change a payslip that
/// was already archived. Follows the LeaveRecordService print pattern, extended with byte storage.</summary>
public sealed class PayslipDocumentService : IPayslipDocumentService
{
    public const string PayslipTemplateCode = "DOC_PAYSLIP";
    public const string EntityType = "PayrollPayslip";

    private readonly ApplicationDbContext _db;
    private readonly IDocumentRenderer _renderer;
    private readonly ICurrentUserService _user;
    private readonly IPageTemplateSeeder _pageSeeder;
    private readonly IDocumentLibrarySeeder _libSeeder;

    public PayslipDocumentService(ApplicationDbContext db, IDocumentRenderer renderer, ICurrentUserService user,
        IPageTemplateSeeder pageSeeder, IDocumentLibrarySeeder libSeeder)
    {
        _db = db; _renderer = renderer; _user = user; _pageSeeder = pageSeeder; _libSeeder = libSeeder;
    }

    /// <summary>Resolve the DOC_PAYSLIP template id, lazily seeding the document library on first use so
    /// payslips work out-of-the-box (no manual seed) and never write a GeneratedDocument with an invalid
    /// template FK.</summary>
    private async Task<Guid?> EnsurePayslipTemplateIdAsync(CancellationToken ct)
    {
        var id = await _db.DocumentTemplates.AsNoTracking()
            .Where(t => t.Code == PayslipTemplateCode).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (id is not null) return id;

        var pageId = await _pageSeeder.SeedAsync(ct);
        await _libSeeder.SeedAsync(pageId, ct);
        return await _db.DocumentTemplates.AsNoTracking()
            .Where(t => t.Code == PayslipTemplateCode).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
    }

    // Bilingual labels for the standard seeded components; anything else falls back to its component code.
    private static readonly IReadOnlyDictionary<string, string> StandardLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BASIC"] = "الراتب الأساسي",
            ["HOUSING"] = "بدل السكن",
            ["TRANSPORT"] = "بدل النقل",
            ["TRANSPORTATION"] = "بدل النقل",
            ["GOSI"] = "التأمينات الاجتماعية",
            ["OVERTIME"] = "العمل الإضافي",
            ["LATE"] = "خصم التأخير",
            ["SHORTAGE"] = "خصم النقص",
            ["ABSENCE"] = "خصم الغياب",
            ["ADDITIONS"] = "إضافات",
            ["DEDUCTIONS"] = "استقطاعات",
        };

    public async Task<PayslipPdf> RenderAsync(Guid runId, Guid employeeId, bool archive, CancellationToken ct = default)
    {
        var payslip = await _db.PayrollPayslips.FirstOrDefaultAsync(
            p => p.PayrollRunId == runId && p.EmployeeId == employeeId, ct)
            ?? throw new KeyNotFoundException("Payslip not found for this run and employee.");

        // Serve the frozen archive when present and not re-archiving.
        if (!archive)
        {
            var existing = await _db.Set<GeneratedDocument>().AsNoTracking()
                .FirstOrDefaultAsync(g => g.EntityType == EntityType && g.EntityId == payslip.Id, ct);
            if (existing?.FileUrl is { } url && TryGetFileId(url, out var fid))
            {
                var sf = await _db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fid, ct);
                if (sf is not null) return new PayslipPdf(sf.Data, existing.FileName ?? DefaultFileName(payslip));
            }
        }

        var render = await BuildRenderRequestAsync(runId, payslip, ct);
        var (pdf, fileName) = await _renderer.RenderDocumentAsync(render, ct);

        if (archive) await ArchiveAsync(payslip, render.TemplateId, pdf, fileName, ct);
        return new PayslipPdf(pdf, fileName);
    }

    public async Task<int> ArchiveRunAsync(Guid runId, CancellationToken ct = default)
    {
        var employeeIds = await _db.PayrollPayslips.Where(p => p.PayrollRunId == runId)
            .Select(p => p.EmployeeId).ToListAsync(ct);
        foreach (var eid in employeeIds)
            await RenderAsync(runId, eid, archive: true, ct);
        return employeeIds.Count;
    }

    private async Task<DocumentRenderRequest> BuildRenderRequestAsync(Guid runId, PayrollPayslip payslip, CancellationToken ct)
    {
        var run = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == payslip.EmployeeId, ct);
        var company = await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct);

        string? dept = emp?.DepartmentId is { } dep
            ? await _db.Departments.AsNoTracking().Where(d => d.Id == dep).Select(d => d.NameAr ?? d.Name).FirstOrDefaultAsync(ct) : null;
        string? job = emp?.JobTitleId is { } jt
            ? await _db.Positions.AsNoTracking().Where(p => p.Id == jt).Select(p => p.NameAr ?? p.NameEn).FirstOrDefaultAsync(ct) : null;
        string? payMethod = emp?.PaymentMethodId is { } pm
            ? await _db.MasterDataItems.AsNoTracking().Where(m => m.Id == pm).Select(m => m.NameAr).FirstOrDefaultAsync(ct) : null;

        var breakdown = PayslipComponentProjection.Project(payslip.ComponentsJson);
        var labels = BuildLabels(breakdown);

        var ctx = new PayslipTokenContext(
            EmployeeName: payslip.EmployeeName, EmployeeNumber: payslip.EmployeeNumber,
            Department: dept, Position: job, NationalId: emp?.NationalId, Iban: emp?.Iban, PaymentMethod: payMethod,
            PeriodYear: run?.TargetPeriodYear ?? 0, PeriodMonth: run?.TargetPeriodMonth ?? 0,
            RunNumber: run?.RunNumber ?? "—", PayDate: run?.ApprovedAt,
            GrossEarnings: payslip.GrossEarnings, TotalDeductions: payslip.TotalDeductions,
            NetAmount: payslip.NetAmount, Currency: payslip.Currency,
            GeneratedAt: DateTime.UtcNow, GeneratedBy: _user.Email,
            CompanyNameAr: company?.NameAr, CompanyNameEn: company?.NameEn,
            CompanyCr: company?.CommercialRegistration, CompanyVat: company?.VatNumber,
            CompanyPhone: company?.Phone, CompanyEmail: company?.Email,
            CompanyWebsite: company?.Website, CompanyAddress: company?.Address);

        var tokens = PayslipTokens.Build(ctx);
        var templateId = await EnsurePayslipTemplateIdAsync(ct);

        return new DocumentRenderRequest(
            TemplateId: templateId,
            FallbackTitle: "قسيمة راتب",
            RefNumber: run?.RunNumber ?? "",
            Tokens: tokens,
            DefaultDetails: null,
            Approvals: null,
            FileName: DefaultFileName(payslip),
            Payslip: breakdown,
            PayslipLabels: labels);
    }

    private static Dictionary<string, string> BuildLabels(PayslipBreakdown b)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in b.Earnings.Concat(b.Deductions))
            if (StandardLabels.TryGetValue(line.ComponentCode, out var lbl))
                labels[line.ComponentCode] = lbl;
        return labels;
    }

    private async Task ArchiveAsync(PayrollPayslip payslip, Guid? templateId, byte[] pdf, string fileName, CancellationToken ct)
    {
        // GeneratedDocument.DocumentTemplateId is a required FK — never persist an archive record without a
        // real template (would violate the FK → 500). EnsurePayslipTemplateIdAsync makes this effectively
        // always set; this guard is defense-in-depth.
        if (templateId is not { } tid) return;

        var stored = new StoredFile
        {
            TenantId = _user.TenantId,
            FileName = fileName,
            ContentType = "application/pdf",
            Data = pdf,
            SizeBytes = pdf.LongLength,
            Category = "payslip",
        };
        _db.Files.Add(stored);

        var doc = await _db.Set<GeneratedDocument>()
            .FirstOrDefaultAsync(g => g.EntityType == EntityType && g.EntityId == payslip.Id, ct);
        if (doc is null)
        {
            doc = new GeneratedDocument { EntityType = EntityType, EntityId = payslip.Id };
            _db.Set<GeneratedDocument>().Add(doc);
        }
        doc.DocumentTemplateId = tid;
        doc.Status = DocumentGenerationStatus.Completed;
        doc.OutputFormat = DocumentOutputFormat.Pdf;
        doc.FileUrl = $"/api/files/{stored.Id}";
        doc.FileName = fileName;
        doc.FileSizeBytes = pdf.LongLength;
        doc.GeneratedAt = DateTime.UtcNow;
        doc.GeneratedById = _user.UserId;

        await _db.SaveChangesAsync(ct);
    }

    private static string DefaultFileName(PayrollPayslip p) => $"payslip-{p.EmployeeNumber}-{p.Id:N}.pdf";

    private static bool TryGetFileId(string url, out Guid id)
    {
        id = Guid.Empty;
        var slash = url.LastIndexOf('/');
        var tail = slash >= 0 ? url[(slash + 1)..] : url;
        return Guid.TryParse(tail, out id);
    }
}
