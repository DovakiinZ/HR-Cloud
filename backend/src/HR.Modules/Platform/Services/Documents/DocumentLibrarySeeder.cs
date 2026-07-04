using System.Text.Json;
using HR.Domain.Engines.Documents;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Documents;

/// <summary>Seeds the ready-to-use document library (block-model, published, IsSystem). System
/// templates can be duplicated/edited but not deleted. Idempotent on Code.</summary>
public interface IDocumentLibrarySeeder
{
    Task SeedAsync(Guid defaultPageTemplateId, CancellationToken ct);
}

public sealed class DocumentLibrarySeeder : IDocumentLibrarySeeder
{
    private readonly ApplicationDbContext _db;
    public DocumentLibrarySeeder(ApplicationDbContext db) => _db = db;

    // ── block builders ──
    private static object Title(string t) => new { type = "title", text = t, align = "center", size = "lg" };
    private static object P(string t) => new { type = "text", text = t, align = "right" };
    private static object Bold(string t) => new { type = "text", text = t, align = "right", bold = true };
    private static object Row(string label, string value) => new { label, value };
    private static object Table(params object[] rows) => new { type = "table", rows };
    private static object Divider() => new { type = "divider" };
    private static object Spacer(int h = 16) => new { type = "spacer", height = h };
    private static object Sig(string role, string label) => new { type = "signature", role, label };
    private static object Components(string label) => new { type = "components", label };
    private static object Qr() => new { type = "qr", align = "center" };
    private static string Layout(params object[] blocks) => JsonSerializer.Serialize(new { blocks });

    public async Task SeedAsync(Guid pageTemplateId, CancellationToken ct)
    {
        await Ensure("DOC_SALARY_CERTIFICATE", "Salary Certificate", "شهادة تعريف بالراتب", pageTemplateId, Layout(
            Title("شهادة تعريف بالراتب"),
            Spacer(),
            P("تشهد {{Company.Name}} بأن الموظف/ة {{Employee.FullName}} يعمل/تعمل لدينا وفق البيانات التالية:"),
            Table(
                Row("الرقم الوظيفي", "{{Employee.EmployeeNumber}}"),
                Row("المسمى الوظيفي", "{{Employee.JobTitle}}"),
                Row("الإدارة", "{{Employee.Department}}"),
                Row("تاريخ التعيين", "{{Employee.HireDate}}"),
                Row("الراتب الأساسي", "{{Payroll.BasicSalary}} {{Payroll.Currency}}"),
                Row("بدل السكن", "{{Payroll.HousingAllowance}} {{Payroll.Currency}}"),
                Row("بدل النقل", "{{Payroll.TransportationAllowance}} {{Payroll.Currency}}"),
                Row("إجمالي الراتب", "{{Payroll.TotalSalary}} {{Payroll.Currency}}")),
            Spacer(),
            P("وقد أُعطيت هذه الشهادة بناءً على طلبه/طلبها دون أدنى مسؤولية على الشركة."),
            Spacer(24),
            Sig("hr", "إدارة الموارد البشرية")), ct);

        await Ensure("DOC_EMPLOYMENT_CERTIFICATE", "Employment Certificate", "شهادة خبرة", pageTemplateId, Layout(
            Title("شهادة خبرة / تعريف بالعمل"),
            Spacer(),
            P("تشهد {{Company.Name}} بأن الموظف/ة {{Employee.FullName}} (رقم {{Employee.EmployeeNumber}}) يعمل/تعمل لدينا بمسمى {{Employee.JobTitle}} في إدارة {{Employee.Department}} منذ تاريخ {{Employee.HireDate}}."),
            Spacer(),
            P("وتُعطى هذه الشهادة بناءً على طلبه/طلبها لتقديمها لمن يهمه الأمر."),
            Spacer(24),
            Sig("hr", "إدارة الموارد البشرية")), ct);

        await Ensure("DOC_LEAVE_APPROVAL", "Leave Approval Letter", "خطاب موافقة إجازة", pageTemplateId, Layout(
            Title("موافقة على طلب إجازة"),
            Spacer(),
            P("نفيدكم بأنه تمت الموافقة على طلب الإجازة المقدّم من الموظف/ة {{Employee.FullName}} (رقم {{Employee.EmployeeNumber}})."),
            Table(
                Row("نوع الإجازة", "{{Leave.Type}}"),
                Row("من تاريخ", "{{Leave.StartDate}}"),
                Row("إلى تاريخ", "{{Leave.EndDate}}"),
                Row("عدد الأيام", "{{Leave.Days}}"),
                Row("رقم الطلب", "{{Request.Number}}")),
            Spacer(24),
            Sig("hr", "إدارة الموارد البشرية")), ct);

        await Ensure("DOC_LEAVE_RECORD", "Leave Record", "سجل إجازة", pageTemplateId, Layout(
            Title("سجل إجازة"),
            Spacer(),
            P("نُفيد بأن الموظف/ة {{Employee.FullName}} (رقم {{Employee.EmployeeNumber}}) — {{Employee.JobTitle}} بإدارة {{Employee.Department}} — لديه/لديها سجل الإجازة التالي:"),
            Table(
                Row("رقم السجل", "{{Leave.RecordNumber}}"),
                Row("نوع الإجازة", "{{Leave.Type}}"),
                Row("من تاريخ", "{{Leave.StartDate}}"),
                Row("إلى تاريخ", "{{Leave.EndDate}}"),
                Row("عدد الأيام", "{{Leave.Days}}"),
                Row("الرصيد قبل الإجازة", "{{Leave.BalanceBefore}}"),
                Row("المخصوم", "{{Leave.DaysDeducted}}"),
                Row("الرصيد بعد الاعتماد", "{{Leave.BalanceAfter}}"),
                Row("تاريخ الاعتماد", "{{Leave.ApprovedDate}}"),
                Row("اعتمدها", "{{Leave.ApprovedBy}}"),
                Row("ملاحظات", "{{Leave.Notes}}")),
            Spacer(24),
            Sig("hr", "إدارة الموارد البشرية")), ct);

        await Ensure("DOC_MISSION_LETTER", "Business Trip / Mission Letter", "خطاب مهمة عمل", pageTemplateId, Layout(
            Title("خطاب تكليف بمهمة عمل"),
            Spacer(),
            P("تكلّف {{Company.Name}} الموظف/ة {{Employee.FullName}} (رقم {{Employee.EmployeeNumber}}) بمهمة عمل رسمية وفق التفاصيل المرفقة بالطلب رقم {{Request.Number}}."),
            Spacer(),
            P("نأمل من الجهات المعنية تقديم كل تسهيل ممكن."),
            Spacer(24),
            Sig("ceo", "الإدارة التنفيذية")), ct);

        await Ensure("DOC_LOAN_APPROVAL", "Loan Approval Letter", "خطاب موافقة قرض", pageTemplateId, Layout(
            Title("موافقة على طلب قرض"),
            Spacer(),
            P("نفيدكم بالموافقة على طلب القرض المقدّم من الموظف/ة {{Employee.FullName}} (رقم {{Employee.EmployeeNumber}}) ضمن الطلب رقم {{Request.Number}}."),
            Spacer(24),
            Sig("hr", "إدارة الموارد البشرية"),
            Sig("ceo", "الإدارة المالية")), ct);

        await Ensure("DOC_OVERTIME_APPROVAL", "Overtime Approval", "موافقة عمل إضافي", pageTemplateId, Layout(
            Title("موافقة على عمل إضافي"),
            Spacer(),
            P("تمت الموافقة على العمل الإضافي للموظف/ة {{Employee.FullName}} (رقم {{Employee.EmployeeNumber}}) وفق الطلب رقم {{Request.Number}} بتاريخ {{Request.ApprovalDate}}."),
            Spacer(24),
            Sig("hr", "إدارة الموارد البشرية")), ct);

        await Ensure("DOC_CLEARANCE_FORM", "Clearance Form", "نموذج إخلاء طرف", pageTemplateId, Layout(
            Title("نموذج إخلاء طرف"),
            Spacer(),
            P("يُقر بأن الموظف/ة {{Employee.FullName}} (رقم {{Employee.EmployeeNumber}}) — {{Employee.JobTitle}} — قد أخلى طرفه من الجهات التالية:"),
            Table(
                Row("الإدارة المباشرة", ""),
                Row("تقنية المعلومات", ""),
                Row("المالية", ""),
                Row("الموارد البشرية", "")),
            Spacer(24),
            Sig("hr", "إدارة الموارد البشرية")), ct);

        await Ensure("DOC_CONTRACT_RENEWAL", "Contract Renewal", "تجديد عقد", pageTemplateId, Layout(
            Title("خطاب تجديد عقد عمل"),
            Spacer(),
            P("يسر {{Company.Name}} إفادتكم بتجديد عقد العمل الخاص بالموظف/ة {{Employee.FullName}} (رقم {{Employee.EmployeeNumber}}) بمسمى {{Employee.JobTitle}}."),
            Spacer(24),
            Sig("ceo", "الإدارة التنفيذية")), ct);

        await Ensure("DOC_JOINING_LETTER", "Joining Letter", "خطاب مباشرة عمل", pageTemplateId, Layout(
            Title("خطاب مباشرة عمل"),
            Spacer(),
            P("نفيدكم بأن الموظف/ة {{Employee.FullName}} (رقم {{Employee.EmployeeNumber}}) قد باشر/ت العمل لدى {{Company.Name}} بمسمى {{Employee.JobTitle}} في إدارة {{Employee.Department}} اعتباراً من {{Employee.HireDate}}."),
            Spacer(24),
            Sig("hr", "إدارة الموارد البشرية")), ct);

        // ── Payslip (SP4): a Payroll-module template using the dynamic "components" block, so any set of
        // earnings/deductions renders without code changes. Editing it never changes archived payslips.
        await Ensure("DOC_PAYSLIP", "Payslip", "قسيمة راتب", pageTemplateId, Layout(
            Title("قسيمة راتب / Payslip"),
            P("{{Company.NameAr}} — {{Company.NameEn}}"),
            Spacer(8),
            Table(
                Row("الموظف", "{{Employee.Name}}"),
                Row("الرقم الوظيفي", "{{Employee.Number}}"),
                Row("الإدارة", "{{Employee.Department}}"),
                Row("المسمى الوظيفي", "{{Employee.Position}}"),
                Row("رقم الهوية", "{{Employee.NationalId}}"),
                Row("الآيبان", "{{Employee.IBAN}}"),
                Row("طريقة الدفع", "{{Employee.PaymentMethod}}"),
                Row("فترة الراتب", "{{Payroll.Period}}"),
                Row("تاريخ الصرف", "{{Payroll.PayDate}}"),
                Row("رقم المسيّر", "{{Payroll.RunNumber}}")),
            Spacer(8),
            Components("الإيرادات"),
            Spacer(16),
            Qr(),
            P("رقم المسيّر: {{Payroll.RunNumber}} — تم الإنشاء: {{Payroll.GeneratedAt}} بواسطة {{Payroll.GeneratedBy}}"),
            Spacer(16),
            Sig("hr", "إدارة الموارد البشرية")), ct, module: "Payroll");

        await _db.SaveChangesAsync(ct);
    }

    private async Task Ensure(string code, string en, string ar, Guid pageTemplateId, string layoutJson, CancellationToken ct, string module = "Requests")
    {
        var existing = await _db.DocumentTemplates.FirstOrDefaultAsync(d => d.Code == code, ct);
        if (existing is not null)
        {
            // Upgrade legacy HTML library entries to the block model + page template, once.
            existing.IsSystem = true;
            if (string.IsNullOrWhiteSpace(existing.LayoutJson)) existing.LayoutJson = layoutJson;
            existing.PageTemplateId ??= pageTemplateId;
            return;
        }
        _db.DocumentTemplates.Add(new DocumentTemplate
        {
            Code = code, NameEn = en, NameAr = ar, Module = module,
            Status = DocumentTemplateStatus.Published, OutputFormat = DocumentOutputFormat.Pdf,
            LayoutJson = layoutJson, PageTemplateId = pageTemplateId,
            UseBranding = true, IsActive = true, IsSystem = true,
        });
    }
}
