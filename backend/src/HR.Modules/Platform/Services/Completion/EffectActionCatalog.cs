using HR.Application.Engines.Completion;
using HR.Domain.Enums;

namespace HR.Modules.Platform.Services.Completion;

/// <summary>
/// The code-defined Effect Action Catalog. Adding an action means adding a descriptor here and an
/// IEffectExecutor in the owning module — the builder, validation and APIs all read from this.
/// </summary>
public sealed class EffectActionCatalog : IEffectActionCatalog
{
    private static readonly IReadOnlySet<EffectTrigger> FinalOnly =
        new HashSet<EffectTrigger> { EffectTrigger.FinalApproval };

    private static readonly IReadOnlySet<EffectTrigger> AnyTrigger =
        new HashSet<EffectTrigger> { EffectTrigger.FinalApproval, EffectTrigger.Rejection, EffectTrigger.Cancellation };

    // Common source sets. Identity-bearing inputs deliberately exclude Constant: an employee id
    // typed as a literal into a request type would apply to every employee who submits it.
    private static readonly IReadOnlySet<EffectValueSource> ContextOnly =
        new HashSet<EffectValueSource> { EffectValueSource.RequestContext };

    private static readonly IReadOnlySet<EffectValueSource> FieldOrContext =
        new HashSet<EffectValueSource> { EffectValueSource.FormField, EffectValueSource.RequestContext };

    private static readonly IReadOnlySet<EffectValueSource> FieldContextOrConstant =
        new HashSet<EffectValueSource> { EffectValueSource.FormField, EffectValueSource.RequestContext, EffectValueSource.Constant };

    private static EffectInputDescriptor In(string key, string ar, string en, bool required, IReadOnlySet<EffectValueSource> sources)
        => new(key, ar, en, required, sources);

    private static readonly EffectActionDescriptor[] Descriptors =
    {
        new()
        {
            EffectType = EffectTypes.LeaveCreateApprovedLeave,
            LabelAr = "إنشاء إجازة معتمدة", LabelEn = "Create approved leave",
            DescriptionAr = "ينشئ سجل إجازة معتمدة ويخصم الرصيد.",
            DescriptionEn = "Creates an approved leave record and deducts the balance.",
            Module = "Leave",
            SupportedTriggers = FinalOnly,
            ExecutionMode = EffectExecutionMode.Transactional,
            Inputs = new[]
            {
                In("leaveTypeId", "نوع الإجازة", "Leave type", true, FieldOrContext),
                In("startDate", "تاريخ البداية", "Start date", true, FieldOrContext),
                In("endDate", "تاريخ النهاية", "End date", true, FieldOrContext),
                In("daysCount", "عدد الأيام", "Days", false, FieldOrContext),
                In("affectsBalance", "يخصم من الرصيد", "Deducts balance", false, FieldContextOrConstant),
                In("notes", "ملاحظات", "Notes", false, FieldContextOrConstant),
            },
            RequiredPermissions = new[] { "Leaves.Create" },
        },
        new()
        {
            EffectType = EffectTypes.AttendanceApplyLeaveDays,
            LabelAr = "تعليم أيام الإجازة في الحضور", LabelEn = "Mark leave days in attendance",
            DescriptionAr = "يعلّم كل يوم ضمن الفترة كإجازة في سجل الحضور.",
            DescriptionEn = "Marks every day in the range as on-leave in attendance.",
            Module = "Attendance",
            SupportedTriggers = FinalOnly,
            ExecutionMode = EffectExecutionMode.Transactional,
            Inputs = new[]
            {
                In("startDate", "تاريخ البداية", "Start date", true, FieldOrContext),
                In("endDate", "تاريخ النهاية", "End date", true, FieldOrContext),
            },
            RequiredPermissions = new[] { "Attendance.Edit" },
        },
        new()
        {
            EffectType = EffectTypes.AttendanceCreatePunch,
            LabelAr = "إنشاء بصمة حضور", LabelEn = "Create attendance punch",
            DescriptionAr = "ينشئ سجل حضور بوقت دخول وخروج.",
            DescriptionEn = "Creates an attendance record with check-in and check-out times.",
            Module = "Attendance",
            SupportedTriggers = FinalOnly,
            ExecutionMode = EffectExecutionMode.Transactional,
            Inputs = new[]
            {
                In("date", "التاريخ", "Date", true, FieldOrContext),
                In("checkIn", "وقت الحضور", "Check-in", false, FieldOrContext),
                In("checkOut", "وقت الانصراف", "Check-out", false, FieldOrContext),
                In("reason", "السبب", "Reason", false, FieldContextOrConstant),
            },
            RequiredPermissions = new[] { "Attendance.Create" },
        },
        new()
        {
            EffectType = EffectTypes.AttendanceCorrect,
            LabelAr = "تصحيح الحضور", LabelEn = "Correct attendance",
            DescriptionAr = "يصحّح يوم حضور ويلغي التأخير والنقص.",
            DescriptionEn = "Corrects an attendance day, clearing late and shortage minutes.",
            Module = "Attendance",
            SupportedTriggers = FinalOnly,
            ExecutionMode = EffectExecutionMode.Transactional,
            Inputs = new[]
            {
                In("date", "التاريخ", "Date", true, FieldOrContext),
                In("reason", "السبب", "Reason", false, FieldContextOrConstant),
            },
            RequiredPermissions = new[] { "Attendance.Edit" },
        },
        new()
        {
            EffectType = EffectTypes.ExpenseCreateClaim,
            LabelAr = "إنشاء مطالبة مصروف", LabelEn = "Create expense claim",
            DescriptionAr = "ينشئ مطالبة مصروف معتمدة للموظف.",
            DescriptionEn = "Creates an approved expense claim for the employee.",
            Module = "Expenses",
            SupportedTriggers = FinalOnly,
            ExecutionMode = EffectExecutionMode.Transactional,
            Inputs = new[]
            {
                In("expenseCategory", "فئة المصروف", "Expense category", true, FieldOrContext),
                In("amount", "المبلغ", "Amount", true, FieldOrContext),
                In("description", "الوصف", "Description", false, FieldContextOrConstant),
                In("receipt", "الإيصال", "Receipt", false, FieldOrContext),
                In("currency", "العملة", "Currency", false, FieldContextOrConstant),
            },
            RequiredPermissions = new[] { "Expenses.Create" },
        },
        new()
        {
            EffectType = EffectTypes.LoanCreate,
            LabelAr = "إنشاء قرض", LabelEn = "Create loan",
            DescriptionAr = "ينشئ قرضًا وجدول أقساطه.",
            DescriptionEn = "Creates a loan and its installment schedule.",
            Module = "Loans",
            SupportedTriggers = FinalOnly,
            ExecutionMode = EffectExecutionMode.Transactional,
            Inputs = new[]
            {
                In("loanType", "نوع القرض", "Loan type", true, FieldOrContext),
                In("amount", "المبلغ", "Amount", true, FieldOrContext),
                In("installmentMonths", "عدد الأقساط", "Installments", false, FieldContextOrConstant),
                In("kind", "التصنيف", "Kind", false, FieldContextOrConstant),
            },
            RequiredPermissions = new[] { "Loans.Create" },
        },
        new()
        {
            EffectType = EffectTypes.AssetsAssignCustody,
            LabelAr = "إسناد عهدة", LabelEn = "Assign custody",
            DescriptionAr = "يسند أصلًا كعهدة للموظف مرتبطة بالطلب.",
            DescriptionEn = "Assigns an asset to the employee as custody, linked to the request.",
            Module = "Assets",
            SupportedTriggers = FinalOnly,
            ExecutionMode = EffectExecutionMode.Transactional,
            Inputs = new[]
            {
                In("assetId", "الأصل", "Asset", true, FieldOrContext),
                In("employeeId", "الموظف", "Employee", true, ContextOnly),
                In("expectedReturnDate", "تاريخ الإرجاع المتوقع", "Expected return date", false, FieldOrContext),
                In("notes", "ملاحظات", "Notes", false, FieldContextOrConstant),
            },
            RequiredPermissions = new[] { "Platform.MasterData.Edit" },
        },
        new()
        {
            EffectType = EffectTypes.EmployeeUpdateField,
            LabelAr = "تحديث بيانات الموظف", LabelEn = "Update employee field",
            DescriptionAr = "يحدّث حقلاً معتمداً في ملف الموظف (الهاتف، العنوان، المدينة، جهة اتصال الطوارئ) مع حفظ القيمة السابقة. لا يمكنه تعديل الراتب أو الحساب البنكي أو الحالة.",
            DescriptionEn = "Updates a whitelisted employee profile field (phone, address, city, emergency contact), keeping the previous value. Cannot touch salary, bank or status.",
            Module = "Employees",
            SupportedTriggers = FinalOnly,
            ExecutionMode = EffectExecutionMode.Transactional,
            Inputs = new[]
            {
                In("fieldKey", "الحقل المطلوب تحديثه", "Field to update", true, FieldContextOrConstant),
                In("newValue", "القيمة الجديدة", "New value", true, FieldContextOrConstant),
            },
            RequiredPermissions = new[] { "Employees.Edit" },
        },
        new()
        {
            EffectType = EffectTypes.NotificationSend,
            LabelAr = "إرسال إشعار", LabelEn = "Send notification",
            DescriptionAr = "يرسل إشعارًا بالبريد. يُنفَّذ لاحقًا، ولا يؤثر فشله على الطلب.",
            DescriptionEn = "Sends an email notification. Delivered out of band; a failure never affects the request.",
            Module = "Notifications",
            SupportedTriggers = AnyTrigger,
            ExecutionMode = EffectExecutionMode.Asynchronous,
            Inputs = new[]
            {
                In("toEmail", "البريد الإلكتروني", "Recipient email", false, FieldContextOrConstant),
                In("subject", "الموضوع", "Subject", true, FieldContextOrConstant),
                In("body", "النص", "Body", true, FieldContextOrConstant),
            },
            RequiredPermissions = new[] { "Notifications.Create" },
        },
        new()
        {
            EffectType = EffectTypes.TaskCreate,
            LabelAr = "إنشاء مهمة", LabelEn = "Create task",
            DescriptionAr = "ينشئ مهمة متابعة مرتبطة بالطلب.",
            DescriptionEn = "Creates a follow-up task linked to the request.",
            Module = "Tasks",
            SupportedTriggers = AnyTrigger,
            ExecutionMode = EffectExecutionMode.Transactional,
            Inputs = new[]
            {
                In("title", "العنوان", "Title", true, FieldContextOrConstant),
                In("description", "الوصف", "Description", false, FieldContextOrConstant),
                In("assigneeUserId", "المسؤول", "Assignee", false, new HashSet<EffectValueSource>
                    { EffectValueSource.RequestContext, EffectValueSource.CurrentUser, EffectValueSource.Constant }),
                In("dueDate", "تاريخ الاستحقاق", "Due date", false, FieldOrContext),
            },
            RequiredPermissions = new[] { "Tasks.Create" },
        },
    };

    private static readonly Dictionary<string, EffectActionDescriptor> ByType =
        Descriptors.ToDictionary(d => d.EffectType, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<EffectActionDescriptor> All() => Descriptors;

    public EffectActionDescriptor? Find(string effectType)
        => effectType is not null && ByType.TryGetValue(effectType, out var d) ? d : null;

    public bool IsKnown(string effectType) => Find(effectType) is not null;
}
