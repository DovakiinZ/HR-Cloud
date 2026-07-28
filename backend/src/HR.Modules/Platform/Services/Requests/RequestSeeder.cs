using System.Text.Json;
using HR.Domain.Engines.Documents;
using HR.Domain.Engines.Forms;
using HR.Domain.Engines.MasterData;
using HR.Domain.Engines.Requests;
using HR.Domain.Engines.Workflows;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Requests;

/// <summary>
/// Provisions built-in System Requests fully (form + workflow + impact + print template).
/// Everything is object-referenced (category/leave-type via master data), never free text.
/// </summary>
public sealed class RequestSeeder : IRequestSeeder
{
    private readonly ApplicationDbContext _db;
    private readonly HR.Modules.Platform.Services.Documents.IPageTemplateSeeder _pageSeeder;
    private readonly HR.Modules.Platform.Services.Documents.IDocumentLibrarySeeder _libSeeder;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public RequestSeeder(ApplicationDbContext db,
        HR.Modules.Platform.Services.Documents.IPageTemplateSeeder pageSeeder,
        HR.Modules.Platform.Services.Documents.IDocumentLibrarySeeder libSeeder)
    {
        _db = db; _pageSeeder = pageSeeder; _libSeeder = libSeeder;
    }

    public async Task<int> SeedSystemRequestsAsync(CancellationToken ct)
    {
        // 0) Object references (created if missing — governance: requests reference these).
        var catTimeOff = await EnsureCategory("TIME_OFF", "الإجازات والحضور", "Time Off & Attendance", ct);
        var catFinance = await EnsureCategory("FINANCE", "المالية والرواتب", "Finance & Payroll", ct);
        var catLetters = await EnsureCategory("LETTERS", "الخطابات والشهادات", "Letters & Certificates", ct);
        var catHr = await EnsureCategory("HR", "الموارد البشرية والشخصية", "HR & Personal", ct);

        await EnsureLeaveTypesAsync(ct);
        await EnsureExpenseCategoriesAsync(ct);
        await EnsureLoanTypesAsync(ct);

        // 1) Page-template presets (chrome) + ready-to-use document library (block model).
        var defaultPage = await _pageSeeder.SeedAsync(ct);
        await _libSeeder.SeedAsync(defaultPage, ct);

        // 2) Workflows (separate entities — requests reference them by id).
        var wfManager = await EnsureWorkflow("WF_REQ_MANAGER", "Manager Approval", "موافقة المدير",
            new[] { Step(ApproverType.DirectManager, "المدير المباشر", "Direct Manager") }, ct);
        var wfMgrFinPay = await EnsureWorkflow("WF_REQ_MGR_FIN_PAY", "Manager → Finance → Payroll", "المدير ← المالية ← الرواتب",
            new[] { Step(ApproverType.DirectManager, "المدير المباشر", "Direct Manager"), Step(ApproverType.SystemAdmin, "المالية", "Finance"), Step(ApproverType.SystemAdmin, "الرواتب", "Payroll") }, ct);
        var wfMgrFin = await EnsureWorkflow("WF_REQ_MGR_FIN", "Manager → Finance", "المدير ← المالية",
            new[] { Step(ApproverType.DirectManager, "المدير المباشر", "Direct Manager"), Step(ApproverType.SystemAdmin, "المالية", "Finance") }, ct);
        var wfMgrDeptHr = await EnsureWorkflow("WF_REQ_MGR_DEPT_HR", "Manager → Dept → HR", "المدير ← مدير الإدارة ← الموارد البشرية",
            new[] { Step(ApproverType.DirectManager, "المدير المباشر", "Direct Manager"), Step(ApproverType.DepartmentHead, "مدير الإدارة", "Department Manager"), Step(ApproverType.HrManager, "الموارد البشرية", "HR") }, ct);
        var wfHr = await EnsureWorkflow("WF_REQ_HR", "HR Approval", "موافقة الموارد البشرية",
            new[] { Step(ApproverType.HrManager, "الموارد البشرية", "HR") }, ct);

        // 3) System requests, each fully provisioned. The sub-type is a FIELD (object ref),
        //    not a separate request — one Leave Request, leave type selected inside.
        int created = 0;
        created += await EnsureRequest("LEAVE_REQUEST", "طلب إجازة", "Leave Request", catTimeOff, null, wfManager, null,
            LeaveRequestForm(), Impact(leave: true, attendance: true, document: true), "CalendarDays", "#34D399", ct);
        created += await EnsureRequest("SALARY_CERTIFICATE", "شهادة راتب", "Salary Certificate", catLetters, null, wfHr, null,
            SalaryCertificateForm(), Impact(document: true), "FileText", "#60A5FA", ct);
        created += await EnsureRequest("SALARY_ADVANCE", "سلفة راتب", "Salary Advance", catFinance, null, wfMgrFinPay, null,
            AmountForm("FORM_SALARY_ADVANCE", "نموذج سلفة راتب", "Salary Advance Form"),
            Impact(payroll: true, createsLoan: true, finance: true), "Wallet", "#FBBF24", ct);
        created += await EnsureRequest("LOAN_REQUEST", "طلب قرض", "Loan Request", catFinance, null, wfMgrFin, null,
            LoanForm(), Impact(loans: true, createsLoan: true, finance: true), "HandCoins", "#A78BFA", ct);
        created += await EnsureRequest("ATTENDANCE_CORRECTION", "تصحيح حضور", "Attendance Correction", catTimeOff, null, wfManager, null,
            AttendanceCorrectionForm(), Impact(attendance: true), "Clock", "#22D3EE", ct);
        created += await EnsureRequest("BUSINESS_TRIP", "رحلة عمل", "Business Trip", catHr, null, wfMgrDeptHr, null,
            BusinessTripForm(), Impact(document: false), "Briefcase", "#94A3B8", ct);
        created += await EnsureRequest("EXPENSE_CLAIM", "مطالبة مصروف", "Expense Claim", catFinance, null, wfMgrFin, null,
            ExpenseForm(), Impact(expenses: true, expenseRecord: true, finance: true), "Receipt", "#F472B6", ct);
        created += await EnsureRequest("EMPLOYEE_DATA_UPDATE", "تحديث بيانات", "Employee Data Update", catHr, null, wfHr, null,
            DataUpdateForm(), Impact(), "UserCog", "#A3E635", ct);

        // Additional enterprise request types (single Leave Request keeps leave subtypes as a field).
        created += await EnsureRequest("MISSING_PUNCH", "بصمة ناقصة", "Missing Punch", catTimeOff, null, wfManager, null,
            MissingPunchForm(), Impact(attendancePunch: true), "Fingerprint", "#22D3EE", ct);
        created += await EnsureRequest("OVERTIME_REQUEST", "عمل إضافي", "Overtime Request", catTimeOff, null, wfManager, null,
            OvertimeForm(), Impact(), "Timer", "#FBBF24", ct);
        created += await EnsureRequest("WORK_FROM_HOME", "عمل عن بُعد", "Work From Home", catTimeOff, null, wfManager, null,
            DateRangeForm("FORM_WFH", "نموذج عمل عن بُعد", "Work From Home Form"), Impact(), "House", "#34D399", ct);
        created += await EnsureRequest("RETURN_FROM_LEAVE", "العودة من إجازة", "Return From Leave", catTimeOff, null, wfManager, null,
            ReturnFromLeaveForm(), Impact(), "LogIn", "#60A5FA", ct);
        created += await EnsureRequest("RESIGNATION", "استقالة", "Resignation", catHr, null, wfMgrDeptHr, null,
            ResignationForm(), Impact(document: true), "DoorOpen", "#F87171", ct);
        created += await EnsureRequest("CLEARANCE", "إخلاء طرف", "Clearance", catHr, null, wfMgrDeptHr, null,
            ClearanceForm(), Impact(document: true), "ClipboardCheck", "#94A3B8", ct);
        created += await EnsureRequest("TRAINING_REQUEST", "طلب تدريب", "Training Request", catHr, null, wfMgrFin, null,
            TrainingForm(), Impact(), "GraduationCap", "#A78BFA", ct);
        created += await EnsureRequest("EQUIPMENT_REQUEST", "طلب معدات", "Equipment Request", catHr, null, wfManager, null,
            EquipmentForm(), Impact(), "Laptop", "#38BDF8", ct);
        // Custody: the dynamic-request example. Its behaviour comes entirely from a configured
        // RequestEffectDefinition (Assets.AssignCustody), not from RequestImpactMapping flags —
        // which is the point of it. Impact() is therefore all-false.
        created += await EnsureRequest("CUSTODY_REQUEST", "طلب عهدة", "Custody Request", catHr, null, wfManager, null,
            CustodyForm(), Impact(), "Package", "#22D3EE", ct);
        created += await EnsureRequest("COMPLAINT", "شكوى", "Complaint", catHr, null, wfHr, null,
            ComplaintForm(), Impact(), "MessageSquareWarning", "#FB7185", ct);
        created += await EnsureRequest("CONTRACT_RENEWAL", "تجديد عقد", "Contract Renewal", catHr, null, wfHr, null,
            ContractRenewalForm(), Impact(document: true), "FileSignature", "#4ADE80", ct);
        created += await EnsureRequest("DIRECT_MANAGER_CHANGE", "تغيير المدير المباشر", "Direct Manager Change", catHr, null, wfHr, null,
            ManagerChangeForm(), Impact(), "Users", "#C084FC", ct);

        // Retire the old per-leave-type requests (superseded by the single Leave Request).
        await DeactivateRequestsAsync(new[] { "ANNUAL_LEAVE", "SICK_LEAVE", "EMERGENCY_LEAVE" }, ct);
        await _db.SaveChangesAsync(ct);

        // 4) Default request → template mappings (FinalApproval). System mappings can't be deleted.
        await EnsureMapping("LEAVE_REQUEST", "DOC_LEAVE_APPROVAL", DocumentTriggerEvent.FinalApproval, ct);
        await EnsureMapping("SALARY_CERTIFICATE", "DOC_SALARY_CERTIFICATE", DocumentTriggerEvent.FinalApproval, ct);
        await EnsureMapping("LOAN_REQUEST", "DOC_LOAN_APPROVAL", DocumentTriggerEvent.FinalApproval, ct);
        await EnsureMapping("BUSINESS_TRIP", "DOC_MISSION_LETTER", DocumentTriggerEvent.FinalApproval, ct);
        await EnsureMapping("CLEARANCE", "DOC_CLEARANCE_FORM", DocumentTriggerEvent.FinalApproval, ct);
        await EnsureMapping("CONTRACT_RENEWAL", "DOC_CONTRACT_RENEWAL", DocumentTriggerEvent.FinalApproval, ct);
        await EnsureMapping("RESIGNATION", "DOC_CLEARANCE_FORM", DocumentTriggerEvent.FinalApproval, ct);
        await BackfillLegacyMappingsAsync(ct);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    /// <summary>Idempotently bind a request type to a library template for a trigger, keeping the
    /// legacy PrintTemplateId + GeneratesDocument flag in sync for FinalApproval defaults.</summary>
    private async Task EnsureMapping(string requestCode, string templateCode, DocumentTriggerEvent trigger, CancellationToken ct)
    {
        var typeId = await _db.RequestTypes.Where(t => t.Code == requestCode).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        var tplId = await _db.DocumentTemplates.Where(d => d.Code == templateCode).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct);
        if (typeId is null || tplId is null) return;

        var exists = await _db.RequestTemplateMappings.AnyAsync(
            m => m.RequestTypeId == typeId && m.DocumentTemplateId == tplId && m.TriggerEvent == trigger, ct);
        if (!exists)
            _db.RequestTemplateMappings.Add(new RequestTemplateMapping
            {
                RequestTypeId = typeId.Value, DocumentTemplateId = tplId.Value, TriggerEvent = trigger, IsSystem = true,
            });

        if (trigger == DocumentTriggerEvent.FinalApproval)
        {
            var type = await _db.RequestTypes.Include(t => t.ImpactMapping).FirstAsync(t => t.Id == typeId, ct);
            type.PrintTemplateId = tplId;
            type.ImpactMapping ??= new RequestImpactMapping { RequestTypeId = type.Id };
            type.ImpactMapping.GeneratesDocument = true;
        }
    }

    /// <summary>For any request type that already had a legacy PrintTemplateId but no FinalApproval
    /// mapping (older tenants), create the system mapping so generation keeps working.</summary>
    private async Task BackfillLegacyMappingsAsync(CancellationToken ct)
    {
        var legacy = await _db.RequestTypes
            .Where(t => t.PrintTemplateId != null)
            .Select(t => new { t.Id, TemplateId = t.PrintTemplateId!.Value })
            .ToListAsync(ct);
        foreach (var l in legacy)
        {
            var has = await _db.RequestTemplateMappings.AnyAsync(
                m => m.RequestTypeId == l.Id && m.TriggerEvent == DocumentTriggerEvent.FinalApproval, ct);
            if (!has)
                _db.RequestTemplateMappings.Add(new RequestTemplateMapping
                {
                    RequestTypeId = l.Id, DocumentTemplateId = l.TemplateId,
                    TriggerEvent = DocumentTriggerEvent.FinalApproval, IsSystem = true,
                });
        }
    }

    private async Task DeactivateRequestsAsync(string[] codes, CancellationToken ct)
    {
        var stale = await _db.RequestTypes.Where(t => codes.Contains(t.Code) && t.IsActive).ToListAsync(ct);
        foreach (var t in stale) t.IsActive = false;
    }

    // ── Ensure helpers ──────────────────────────────────────────────────────────

    private async Task<Guid> EnsureCategory(string code, string ar, string en, CancellationToken ct)
        => await EnsureMasterData(MasterDataObjectType.RequestCategory, code, ar, en, ct);

    private async Task<Guid> EnsureMasterData(string objectType, string code, string ar, string en, CancellationToken ct)
    {
        var existing = await _db.MasterDataItems.FirstOrDefaultAsync(m => m.ObjectType == objectType && m.Code == code, ct);
        if (existing is not null) return existing.Id;
        var item = new MasterDataItem { ObjectType = objectType, Code = code, NameAr = ar, NameEn = en, IsSystemDefault = true, IsActive = true };
        _db.MasterDataItems.Add(item);
        await _db.SaveChangesAsync(ct);
        return item.Id;
    }

    /// <summary>Upsert a master-data item, backfilling rules MetadataJson when absent.</summary>
    private async Task EnsureMasterDataWithMeta(string objectType, string code, string ar, string en, string metaJson, CancellationToken ct)
    {
        var existing = await _db.MasterDataItems.FirstOrDefaultAsync(m => m.ObjectType == objectType && m.Code == code, ct);
        if (existing is null)
            _db.MasterDataItems.Add(new MasterDataItem { ObjectType = objectType, Code = code, NameAr = ar, NameEn = en, MetadataJson = metaJson, IsSystemDefault = true, IsActive = true });
        else if (string.IsNullOrWhiteSpace(existing.MetadataJson))
            existing.MetadataJson = metaJson; // backfill rules on items seeded before rules existed
        await _db.SaveChangesAsync(ct);
    }

    // 8 leave types, each carrying its own rules (paid/percentage/maxDays/attachment/...).
    private async Task EnsureLeaveTypesAsync(CancellationToken ct)
    {
        var lt = MasterDataObjectType.LeaveType;
        await EnsureMasterDataWithMeta(lt, "ANNUAL", "إجازة سنوية", "Annual Leave", LeaveRulesJson(maxDays: 30, annual: 30), ct);
        await EnsureMasterDataWithMeta(lt, "SICK", "إجازة مرضية", "Sick Leave", LeaveRulesJson(maxDays: 120, annual: 120, attach: true, payroll: true), ct);
        await EnsureMasterDataWithMeta(lt, "EMERGENCY", "إجازة طارئة", "Emergency Leave", LeaveRulesJson(maxDays: 5, annual: 5), ct);
        await EnsureMasterDataWithMeta(lt, "EXAM", "إجازة اختبارات", "Exam Leave", LeaveRulesJson(maxDays: 10, annual: 10, attach: true), ct);
        await EnsureMasterDataWithMeta(lt, "UNPAID", "إجازة بدون راتب", "Unpaid Leave", LeaveRulesJson(paid: false, pct: 0, maxDays: 90, annual: 0, payroll: true), ct);
        await EnsureMasterDataWithMeta(lt, "MARRIAGE", "إجازة زواج", "Marriage Leave", LeaveRulesJson(maxDays: 5, annual: 5), ct);
        await EnsureMasterDataWithMeta(lt, "DEATH", "إجازة وفاة", "Bereavement Leave", LeaveRulesJson(maxDays: 5, annual: 5), ct);
        await EnsureMasterDataWithMeta(lt, "HAJJ", "إجازة حج", "Hajj Leave", LeaveRulesJson(maxDays: 10, annual: 10), ct);
    }

    private async Task EnsureExpenseCategoriesAsync(CancellationToken ct)
    {
        var t = MasterDataObjectType.ExpenseCategory;
        await EnsureMasterData(t, "TRAVEL", "سفر", "Travel", ct);
        await EnsureMasterData(t, "MEALS", "وجبات", "Meals", ct);
        await EnsureMasterData(t, "ACCOMMODATION", "إقامة", "Accommodation", ct);
        await EnsureMasterData(t, "SUPPLIES", "مستلزمات", "Supplies", ct);
        await EnsureMasterData(t, "OTHER", "أخرى", "Other", ct);
    }

    private async Task EnsureLoanTypesAsync(CancellationToken ct)
    {
        var t = MasterDataObjectType.LoanType;
        await EnsureMasterData(t, "PERSONAL", "قرض شخصي", "Personal Loan", ct);
        await EnsureMasterData(t, "EMERGENCY", "قرض طارئ", "Emergency Loan", ct);
        await EnsureMasterData(t, "HOUSING", "قرض سكني", "Housing Loan", ct);
    }

    private static string LeaveRulesJson(bool paid = true, double pct = 100, double maxDays = 30, double annual = 30,
        bool attach = false, bool payroll = false, bool attendance = true, bool weekends = false)
        => JsonSerializer.Serialize(new LeaveRules
        {
            Paid = paid, PaidPercentage = pct, MaxDays = maxDays, AnnualBalance = annual,
            RequiresAttachment = attach, AffectsPayroll = payroll, AffectsAttendance = attendance, CountWeekends = weekends,
        }, Json);

    /// <summary>Field options descriptor that tells the UI to load choices live from master data.</summary>
    private static string Lookup(string objectType) => $"{{\"lookup\":\"{objectType}\"}}";

    /// <summary>An options descriptor pointing at a platform endpoint rather than master data, for
    /// pickers whose source is a real entity feed. The path is relative to /api/platform/.</summary>
    private static string EndpointLookup(string path) => $"{{\"endpoint\":\"{path}\"}}";

    private static WorkflowStepConfig Step(ApproverType type, string ar, string en)
        => new() { ApproverType = (int)type, NameAr = ar, NameEn = en };

    private async Task<Guid> EnsureWorkflow(string code, string en, string ar, WorkflowStepConfig[] steps, CancellationToken ct)
    {
        var existing = await _db.WorkflowDefinitions.FirstOrDefaultAsync(w => w.Code == code, ct);
        if (existing is not null) return existing.Id;
        var def = new WorkflowDefinition { Code = code, NameEn = en, NameAr = ar, TriggerEntityType = "RequestInstance", IsActive = true };
        _db.WorkflowDefinitions.Add(def);
        await _db.SaveChangesAsync(ct);
        var version = new WorkflowVersion
        {
            WorkflowDefinitionId = def.Id, VersionNumber = 1, IsPublished = true, PublishedAt = DateTime.UtcNow,
            Configuration = JsonSerializer.Serialize(new WorkflowChainConfig { Steps = steps.ToList() }, Json),
        };
        _db.WorkflowVersions.Add(version);
        await _db.SaveChangesAsync(ct);
        return def.Id;
    }

    private async Task<Guid> EnsureForm(FormSpec spec, CancellationToken ct)
    {
        var existing = await _db.FormDefinitions.FirstOrDefaultAsync(f => f.Code == spec.Code, ct);
        if (existing is not null) return existing.Id;
        var form = new FormDefinition { Code = spec.Code, NameEn = spec.NameEn, NameAr = spec.NameAr, Module = "Requests", IsPublished = true, IsActive = true };
        for (int i = 0; i < spec.Fields.Count; i++)
        {
            var f = spec.Fields[i];
            form.Fields.Add(new FormField
            {
                Code = f.Code, NameAr = f.NameAr, NameEn = f.NameEn, FieldType = f.Type,
                IsRequired = f.Required, SortOrder = i, Placeholder = f.Placeholder, Options = f.Options,
            });
        }
        _db.FormDefinitions.Add(form);
        await _db.SaveChangesAsync(ct);
        return form.Id;
    }

    private async Task<int> EnsureRequest(string code, string ar, string en, Guid categoryId, Guid? leaveTypeId,
        Guid workflowId, Guid? printTemplateId, FormSpec formSpec, ImpactSpec impact, string icon, string color, CancellationToken ct)
    {
        if (await _db.RequestTypes.AnyAsync(t => t.Code == code, ct)) return 0;
        var formId = await EnsureForm(formSpec, ct);
        var type = new RequestType
        {
            Code = code, NameAr = ar, NameEn = en, Kind = RequestKind.System, IsSystem = true, IsActive = true,
            CategoryId = categoryId, FormDefinitionId = formId, WorkflowDefinitionId = workflowId,
            PrintTemplateId = printTemplateId, LeaveTypeId = leaveTypeId, Icon = icon, Color = color,
            ImpactMapping = new RequestImpactMapping
            {
                AffectsLeaveBalance = impact.Leave, AffectsAttendance = impact.Attendance, AffectsPayroll = impact.Payroll,
                AffectsExpenses = impact.Expenses, AffectsLoans = impact.Loans, CreatesLoanRecord = impact.CreatesLoan,
                CreatesExpenseRecord = impact.ExpenseRecord, CreatesAttendancePunch = impact.AttendancePunch,
                RequiresFinanceApproval = impact.Finance, GeneratesDocument = impact.Document,
            },
        };
        _db.RequestTypes.Add(type);
        return 1;
    }

    // ── Form definitions (system fields use canonical codes the engine understands) ──

    // One generic Leave Request form — the leave type is a field, sourced live from settings.
    private static FormSpec LeaveRequestForm() => new("FORM_LEAVE_REQUEST", "نموذج طلب إجازة", "Leave Request Form", new()
    {
        F("leaveType", "نوع الإجازة", "Leave Type", FieldType.Dropdown, true, options: Lookup(MasterDataObjectType.LeaveType)),
        F("startDate", "تاريخ البداية", "Start Date", FieldType.Date, true),
        F("endDate", "تاريخ النهاية", "End Date", FieldType.Date, true),
        F("attachment", "مرفق", "Attachment", FieldType.File, false),
        F("notes", "ملاحظات", "Notes", FieldType.TextArea, false),
    });

    private static FormSpec SalaryCertificateForm() => new("FORM_SALARY_CERTIFICATE", "نموذج شهادة راتب", "Salary Certificate Form", new()
    {
        F("addressedTo", "موجهة إلى", "Addressed To", FieldType.Text, true),
        F("purpose", "الغرض", "Purpose", FieldType.TextArea, true),
    });

    private static FormSpec AmountForm(string code, string ar, string en) => new(code, ar, en, new()
    {
        F("amount", "المبلغ", "Amount", FieldType.Currency, true),
        F("reason", "السبب", "Reason", FieldType.TextArea, true),
    });

    private static FormSpec LoanForm() => new("FORM_LOAN_REQUEST", "نموذج طلب قرض", "Loan Request Form", new()
    {
        F("loanType", "نوع القرض", "Loan Type", FieldType.Dropdown, true, options: Lookup(MasterDataObjectType.LoanType)),
        F("amount", "المبلغ", "Amount", FieldType.Currency, true),
        F("installmentMonths", "عدد الأشهر", "Installment Months", FieldType.Number, true),
        F("reason", "السبب", "Reason", FieldType.TextArea, true),
    });

    private static FormSpec AttendanceCorrectionForm() => new("FORM_ATTENDANCE_CORRECTION", "نموذج تصحيح حضور", "Attendance Correction Form", new()
    {
        F("startDate", "التاريخ", "Date", FieldType.Date, true),
        F("reason", "السبب", "Reason", FieldType.TextArea, true),
        F("checkIn", "وقت الحضور (HH:mm)", "Check In (HH:mm)", FieldType.Text, false, placeholder: "08:00"),
        F("checkOut", "وقت الانصراف (HH:mm)", "Check Out (HH:mm)", FieldType.Text, false, placeholder: "17:00"),
    });

    private static FormSpec BusinessTripForm() => new("FORM_BUSINESS_TRIP", "نموذج رحلة عمل", "Business Trip Form", new()
    {
        F("destination", "الوجهة", "Destination", FieldType.Text, true),
        F("startDate", "تاريخ البداية", "Start Date", FieldType.Date, true),
        F("endDate", "تاريخ النهاية", "End Date", FieldType.Date, true),
        F("purpose", "الغرض", "Purpose", FieldType.TextArea, true),
    });

    private static FormSpec ExpenseForm() => new("FORM_EXPENSE_CLAIM", "نموذج مطالبة مصروف", "Expense Claim Form", new()
    {
        F("expenseCategory", "فئة المصروف", "Expense Category", FieldType.Dropdown, true, options: Lookup(MasterDataObjectType.ExpenseCategory)),
        F("amount", "المبلغ", "Amount", FieldType.Currency, true),
        F("description", "الوصف", "Description", FieldType.TextArea, true),
        F("receipt", "الإيصال", "Receipt", FieldType.File, false),
    });

    private static FormSpec DataUpdateForm() => new("FORM_DATA_UPDATE", "نموذج تحديث بيانات", "Data Update Form", new()
    {
        F("reason", "ما الذي تريد تحديثه؟", "What do you want to update?", FieldType.TextArea, true),
    });

    private static FormSpec MissingPunchForm() => new("FORM_MISSING_PUNCH", "نموذج بصمة ناقصة", "Missing Punch Form", new()
    {
        F("startDate", "التاريخ", "Date", FieldType.Date, true),
        F("checkIn", "وقت الحضور (HH:mm)", "Check In (HH:mm)", FieldType.Text, false, placeholder: "08:00"),
        F("checkOut", "وقت الانصراف (HH:mm)", "Check Out (HH:mm)", FieldType.Text, false, placeholder: "17:00"),
        F("reason", "السبب", "Reason", FieldType.TextArea, true),
    });

    private static FormSpec OvertimeForm() => new("FORM_OVERTIME", "نموذج عمل إضافي", "Overtime Form", new()
    {
        F("startDate", "التاريخ", "Date", FieldType.Date, true),
        F("hours", "عدد الساعات", "Hours", FieldType.Number, true),
        F("reason", "السبب", "Reason", FieldType.TextArea, true),
    });

    private static FormSpec DateRangeForm(string code, string ar, string en) => new(code, ar, en, new()
    {
        F("startDate", "تاريخ البداية", "Start Date", FieldType.Date, true),
        F("endDate", "تاريخ النهاية", "End Date", FieldType.Date, true),
        F("reason", "السبب", "Reason", FieldType.TextArea, false),
    });

    private static FormSpec ReturnFromLeaveForm() => new("FORM_RETURN_FROM_LEAVE", "نموذج العودة من إجازة", "Return From Leave Form", new()
    {
        F("startDate", "تاريخ العودة", "Return Date", FieldType.Date, true),
        F("notes", "ملاحظات", "Notes", FieldType.TextArea, false),
    });

    private static FormSpec ResignationForm() => new("FORM_RESIGNATION", "نموذج استقالة", "Resignation Form", new()
    {
        F("lastWorkingDay", "آخر يوم عمل", "Last Working Day", FieldType.Date, true),
        F("reason", "السبب", "Reason", FieldType.TextArea, true),
    });

    private static FormSpec ClearanceForm() => new("FORM_CLEARANCE", "نموذج إخلاء طرف", "Clearance Form", new()
    {
        F("lastWorkingDay", "آخر يوم عمل", "Last Working Day", FieldType.Date, true),
        F("notes", "ملاحظات", "Notes", FieldType.TextArea, false),
    });

    private static FormSpec TrainingForm() => new("FORM_TRAINING", "نموذج طلب تدريب", "Training Request Form", new()
    {
        F("title", "اسم البرنامج", "Program Title", FieldType.Text, true),
        F("provider", "الجهة المقدمة", "Provider", FieldType.Text, false),
        F("amount", "التكلفة", "Cost", FieldType.Currency, false),
        F("reason", "المبرر", "Justification", FieldType.TextArea, true),
    });

    private static FormSpec EquipmentForm() => new("FORM_EQUIPMENT", "نموذج طلب معدات", "Equipment Request Form", new()
    {
        F("equipmentType", "نوع المعدة", "Equipment Type", FieldType.Dropdown, true, options: Lookup(MasterDataObjectType.AssetType)),
        F("reason", "المبرر", "Justification", FieldType.TextArea, true),
    });

    private static FormSpec ComplaintForm() => new("FORM_COMPLAINT", "نموذج شكوى", "Complaint Form", new()
    {
        F("subject", "الموضوع", "Subject", FieldType.Text, true),
        F("description", "التفاصيل", "Details", FieldType.TextArea, true),
    });

    private static FormSpec ContractRenewalForm() => new("FORM_CONTRACT_RENEWAL", "نموذج تجديد عقد", "Contract Renewal Form", new()
    {
        F("endDate", "تاريخ نهاية العقد الجديد", "New Contract End Date", FieldType.Date, true),
        F("notes", "ملاحظات", "Notes", FieldType.TextArea, false),
    });

    private static FormSpec ManagerChangeForm() => new("FORM_MANAGER_CHANGE", "نموذج تغيير المدير", "Manager Change Form", new()
    {
        F("newManager", "المدير الجديد", "New Manager", FieldType.Text, true),
        F("reason", "السبب", "Reason", FieldType.TextArea, true),
    });

    /// <summary>Asset, expected return date, notes — the three fields the custody example needs.
    ///
    /// The asset dropdown reads GET /api/platform/assets/assignable, not a master-data lookup.
    /// MasterDataObjectType.AssetType lists asset *categories* ("Laptop", "Vehicle"), so binding the
    /// picker to it would have offered a category where Assets.AssignCustody expects an asset id —
    /// the effect would then fail at approval time on every custody request.</summary>
    private static FormSpec CustodyForm() => new("FORM_CUSTODY_REQUEST", "نموذج طلب عهدة", "Custody Request Form", new()
    {
        F("assetId", "الأصل", "Asset", FieldType.Dropdown, true, options: EndpointLookup("assets/assignable")),
        F("expectedReturnDate", "تاريخ الإرجاع المتوقع", "Expected Return Date", FieldType.Date, false),
        F("notes", "ملاحظات", "Notes", FieldType.TextArea, false),
    });

    private static FieldSpec F(string code, string ar, string en, FieldType type, bool required, string? placeholder = null, string? options = null)
        => new(code, ar, en, type, required, placeholder, options);

    private static ImpactSpec Impact(bool leave = false, bool attendance = false, bool payroll = false,
        bool expenses = false, bool loans = false, bool createsLoan = false, bool finance = false, bool document = false,
        bool expenseRecord = false, bool attendancePunch = false)
        => new(leave, attendance, payroll, expenses, loans, createsLoan, finance, document, expenseRecord, attendancePunch);

    private sealed record FormSpec(string Code, string NameAr, string NameEn, List<FieldSpec> Fields);
    private sealed record FieldSpec(string Code, string NameAr, string NameEn, FieldType Type, bool Required, string? Placeholder = null, string? Options = null);
    private sealed record ImpactSpec(bool Leave, bool Attendance, bool Payroll, bool Expenses, bool Loans, bool CreatesLoan, bool Finance, bool Document, bool ExpenseRecord, bool AttendancePunch);
}
