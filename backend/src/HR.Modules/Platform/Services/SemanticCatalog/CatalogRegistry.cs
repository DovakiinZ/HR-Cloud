using HR.Application.SemanticCatalog.Contracts;
using static HR.Application.SemanticCatalog.Contracts.SemanticFieldRole;

namespace HR.Modules.Platform.Services.SemanticCatalog;

public static class CatalogRegistry
{
    public static readonly IReadOnlyList<SemanticDomain> Domains = new[]
    {
        new SemanticDomain("employees",  "الموظفون",   "Employees",   "بيانات الموظفين",              "Employee data",                  "Users",      1),
        new SemanticDomain("payroll",    "الرواتب",    "Payroll",     "الرواتب والاستحقاقات",          "Payroll & earnings",             "Wallet",     2),
        new SemanticDomain("attendance", "الحضور",     "Attendance",  "الحضور والانصراف",              "Attendance",                     "Clock",      3),
        new SemanticDomain("leaves",     "الإجازات",   "Leaves",      "أرصدة وطلبات الإجازات",        "Leave balances & requests",      "CalendarDays",4),
        new SemanticDomain("requests",   "الطلبات",    "Requests",    "طلبات الموظفين",               "Employee requests",               "Inbox",      5),
        new SemanticDomain("loans",      "السلف",      "Loans",       "سلف الموظفين",                 "Employee loans",                 "HandCoins",  6),
        new SemanticDomain("expenses",   "المصروفات",  "Expenses",    "مطالبات المصروفات",            "Expense claims",                 "Receipt",    7),
        new SemanticDomain("documents",  "المستندات",  "Documents",   "مستندات الموظفين",             "Employee documents",             "FolderOpen", 8),
        new SemanticDomain("recruitment","التوظيف",    "Recruitment", "التوظيف والتعيين",             "Hiring & recruitment",           "UserPlus",   9),
    };

    public static readonly IReadOnlyList<SemanticFieldGroup> FieldGroups = new[]
    {
        new SemanticFieldGroup("personal_information", "المعلومات الشخصية",   "Personal Information", 1),
        new SemanticFieldGroup("employment",           "التوظيف",             "Employment",           2),
        new SemanticFieldGroup("organization",         "الهيكل التنظيمي",     "Organization",         3),
        new SemanticFieldGroup("payroll",              "الرواتب",             "Payroll",              4),
        new SemanticFieldGroup("attendance",           "الحضور",              "Attendance",           5),
        new SemanticFieldGroup("leave",                "الإجازات",            "Leave",                6),
        new SemanticFieldGroup("documents",            "المستندات",           "Documents",            7),
    };

    public static readonly IReadOnlyList<SemanticObject> Objects = new[]
    {
        // ── Employee ─────────────────────────────────────────────────────────
        new SemanticObject(
            ObjectCode: "Employee", DomainCode: "employees",
            NameAr: "الموظفون", NameEn: "Employees",
            DescriptionAr: "سجل الموظفين", DescriptionEn: "Employee records",
            Icon: "Users",
            Keywords: new[] { "employee","staff","موظف","موظفين" },
            DefaultVisible: true,
            FieldGroups: new[]
            {
                new SemanticFieldGroup("personal_information", "المعلومات الشخصية", "Personal Information", 1),
                new SemanticFieldGroup("employment",           "التوظيف",           "Employment",           2),
                new SemanticFieldGroup("organization",         "الهيكل التنظيمي",   "Organization",         3),
                new SemanticFieldGroup("payroll",              "الرواتب",           "Payroll",              4),
            },
            DefaultSort: new SemanticSort("HireDate", "Descending"),
            DefaultFilters: new[]
            {
                new SemanticFilter("DepartmentId", "الإدارة", "Department", "reference", "Department"),
                new SemanticFilter("BranchId",     "الفرع",   "Branch",     "reference", "Branch"),
            },
            RecommendedMetricCodes: new[] { "total_employees","active_employees","new_employees","employees_by_department","expiring_contracts" },
            RecommendedReportCodes: Array.Empty<string>(),
            RecommendedWidgetCodes: Array.Empty<string>(),
            Fields: new[]
            {
                new SemanticField("Employee","FirstNameAr",       "الاسم الأول",         "First Name",       "","","personal_information",null,new[]{"name","اسم"},           Dimension, true),
                new SemanticField("Employee","Status",            "الحالة",              "Status",           "حالة الموظف","Employment status","employment",null,new[]{"status","حالة"},      Dimension, true),
                new SemanticField("Employee","HireDate",          "تاريخ التعيين",        "Hire Date",        "","","employment",null,new[]{"hire","تعيين"},        Dimension, true),
                new SemanticField("Employee","DepartmentId",      "الإدارة",             "Department",       "","","organization",null,new[]{"department","ادارة"},  Dimension, true),
                new SemanticField("Employee","BranchId",          "الفرع",               "Branch",           "","","organization",null,new[]{"branch","فرع"},        Dimension, true),
                new SemanticField("Employee","JobTitleId",        "المسمى الوظيفي",      "Job Title",        "","","employment",null,new[]{"job","title","وظيفة"},  Dimension, true),
                new SemanticField("Employee","ContractEndDate",   "نهاية العقد",          "Contract End",     "","","employment",null,new[]{"contract","عقد"},       Dimension, true),
                new SemanticField("Employee","BasicSalary",       "الراتب الأساسي",       "Basic Salary",     "","","payroll",null,new[]{"salary","راتب"},          Measure,   true),
            }),

        // ── PayrollPayslip ────────────────────────────────────────────────────
        new SemanticObject(
            ObjectCode: "PayrollPayslip", DomainCode: "payroll",
            NameAr: "قسائم الراتب", NameEn: "Payroll Payslips",
            DescriptionAr: "سجلات قسائم الراتب", DescriptionEn: "Payroll payslip records",
            Icon: "Wallet",
            Keywords: new[] { "payroll","payslip","راتب","مرتب","استحقاق" },
            DefaultVisible: true,
            FieldGroups: new[]
            {
                new SemanticFieldGroup("employment", "التوظيف",  "Employment", 2),
                new SemanticFieldGroup("payroll",    "الرواتب",  "Payroll",    4),
            },
            DefaultSort: new SemanticSort("GrossEarnings", "Descending"),
            DefaultFilters: new[]
            {
                new SemanticFilter("DepartmentId", "الإدارة", "Department", "reference", "Department"),
                new SemanticFilter("BranchId",     "الفرع",   "Branch",     "reference", "Branch"),
            },
            RecommendedMetricCodes: new[] { "gross_payroll","net_payroll","total_deductions" },
            RecommendedReportCodes: Array.Empty<string>(),
            RecommendedWidgetCodes: Array.Empty<string>(),
            Fields: new[]
            {
                new SemanticField("PayrollPayslip","GrossEarnings",  "الاستحقاقات الإجمالية","Gross Earnings",    "","","payroll",null,new[]{"gross","earnings","استحقاق"},    Measure,   true),
                new SemanticField("PayrollPayslip","NetAmount",      "صافي الراتب",           "Net Amount",        "","","payroll",null,new[]{"net","amount","صافي"},            Measure,   true),
                new SemanticField("PayrollPayslip","TotalDeductions","إجمالي الخصومات",       "Total Deductions",  "","","payroll",null,new[]{"deductions","خصومات"},            Measure,   true),
                new SemanticField("PayrollPayslip","EmployeeName",   "اسم الموظف",            "Employee Name",     "","","employment",null,new[]{"employee","name","موظف"},      Dimension, true),
                new SemanticField("PayrollPayslip","Currency",       "العملة",                "Currency",          "","","payroll",null,new[]{"currency","عملة"},                Dimension, true),
            }),

        // ── AttendanceRecord ──────────────────────────────────────────────────
        new SemanticObject(
            ObjectCode: "AttendanceRecord", DomainCode: "attendance",
            NameAr: "سجلات الحضور", NameEn: "Attendance Records",
            DescriptionAr: "سجلات الحضور والانصراف", DescriptionEn: "Attendance records",
            Icon: "Clock",
            Keywords: new[] { "attendance","حضور","غياب","تأخير" },
            DefaultVisible: true,
            FieldGroups: new[]
            {
                new SemanticFieldGroup("attendance", "الحضور", "Attendance", 5),
                new SemanticFieldGroup("employment", "التوظيف","Employment", 2),
            },
            DefaultSort: new SemanticSort("Date", "Descending"),
            DefaultFilters: new[]
            {
                new SemanticFilter("DepartmentId", "الإدارة", "Department", "reference", "Department"),
                new SemanticFilter("BranchId",     "الفرع",   "Branch",     "reference", "Branch"),
                new SemanticFilter("Date",         "التاريخ", "Date",       "date-range", null),
            },
            RecommendedMetricCodes: new[] { "late_employees","absent_employees","overtime_minutes" },
            RecommendedReportCodes: Array.Empty<string>(),
            RecommendedWidgetCodes: Array.Empty<string>(),
            Fields: new[]
            {
                new SemanticField("AttendanceRecord","Status",          "الحالة",                "Status",          "حالة الحضور","Attendance status","attendance",null,new[]{"status","حالة"},  Dimension, true),
                new SemanticField("AttendanceRecord","Date",            "التاريخ",               "Date",            "","","attendance",null,new[]{"date","تاريخ"},                               Dimension, true),
                new SemanticField("AttendanceRecord","LateMinutes",     "دقائق التأخير",          "Late Minutes",    "","","attendance",null,new[]{"late","تاخير","دقائق"},                      Measure,   true),
                new SemanticField("AttendanceRecord","OvertimeMinutes", "دقائق العمل الإضافي",   "Overtime Minutes","","","attendance",null,new[]{"overtime","اضافي","دقائق"},                  Measure,   true),
                new SemanticField("AttendanceRecord","WorkedMinutes",   "دقائق العمل",            "Worked Minutes",  "","","attendance",null,new[]{"worked","عمل","دقائق"},                     Measure,   true),
            }),

        // ── LeaveBalance ──────────────────────────────────────────────────────
        new SemanticObject(
            ObjectCode: "LeaveBalance", DomainCode: "leaves",
            NameAr: "أرصدة الإجازات", NameEn: "Leave Balances",
            DescriptionAr: "أرصدة إجازات الموظفين", DescriptionEn: "Employee leave balances",
            Icon: "CalendarDays",
            Keywords: new[] { "leave","balance","إجازة","رصيد" },
            DefaultVisible: true,
            FieldGroups: new[]
            {
                new SemanticFieldGroup("leave",      "الإجازات", "Leave",      6),
                new SemanticFieldGroup("employment", "التوظيف",  "Employment", 2),
            },
            DefaultSort: new SemanticSort("Year", "Descending"),
            DefaultFilters: new[]
            {
                new SemanticFilter("Year", "السنة", "Year", "select", null),
            },
            RecommendedMetricCodes: new[] { "remaining_leave_balance" },
            RecommendedReportCodes: Array.Empty<string>(),
            RecommendedWidgetCodes: Array.Empty<string>(),
            Fields: new[]
            {
                new SemanticField("LeaveBalance","Year",               "السنة",                   "Year",                "","","leave",null,new[]{"year","سنة"},                   Dimension, true),
                new SemanticField("LeaveBalance","EntitledDays",       "الأيام المستحقة",          "Entitled Days",       "","","leave",null,new[]{"entitled","مستحق"},              Measure,   true),
                new SemanticField("LeaveBalance","UsedDays",           "الأيام المستخدمة",         "Used Days",           "","","leave",null,new[]{"used","مستخدم"},                 Measure,   true),
                new SemanticField("LeaveBalance","CarriedForwardDays", "الأيام المرحلة",            "Carried Forward Days","","","leave",null,new[]{"carried","مرحل"},               Measure,   true),
            }),

        // ── RequestInstance ───────────────────────────────────────────────────
        new SemanticObject(
            ObjectCode: "RequestInstance", DomainCode: "requests",
            NameAr: "الطلبات", NameEn: "Request Instances",
            DescriptionAr: "طلبات الموظفين", DescriptionEn: "Employee request instances",
            Icon: "Inbox",
            Keywords: new[] { "request","طلب","طلبات" },
            DefaultVisible: true,
            FieldGroups: new[]
            {
                new SemanticFieldGroup("employment", "التوظيف", "Employment", 2),
            },
            DefaultSort: new SemanticSort("SubmittedAt", "Descending"),
            DefaultFilters: new[]
            {
                new SemanticFilter("Status", "الحالة", "Status", "select", null),
            },
            RecommendedMetricCodes: new[] { "pending_requests" },
            RecommendedReportCodes: Array.Empty<string>(),
            RecommendedWidgetCodes: Array.Empty<string>(),
            Fields: new[]
            {
                new SemanticField("RequestInstance","Status",        "الحالة",        "Status",         "حالة الطلب","Request status","employment",null,new[]{"status","حالة"},   Dimension,  true),
                new SemanticField("RequestInstance","RequestNumber", "رقم الطلب",     "Request Number", "","","employment",null,new[]{"number","رقم"},                           Identifier, true),
                new SemanticField("RequestInstance","SubmittedAt",   "تاريخ التقديم", "Submitted At",   "","","employment",null,new[]{"submitted","تقديم"},                      Dimension,  true),
            }),

        // ── Loan ──────────────────────────────────────────────────────────────
        new SemanticObject(
            ObjectCode: "Loan", DomainCode: "loans",
            NameAr: "السلف", NameEn: "Loans",
            DescriptionAr: "سلف الموظفين", DescriptionEn: "Employee loans",
            Icon: "HandCoins",
            Keywords: new[] { "loan","سلفة","سلف" },
            DefaultVisible: true,
            FieldGroups: new[]
            {
                new SemanticFieldGroup("employment", "التوظيف", "Employment", 2),
                new SemanticFieldGroup("payroll",    "الرواتب", "Payroll",    4),
            },
            DefaultSort: new SemanticSort("Amount", "Descending"),
            DefaultFilters: new[]
            {
                new SemanticFilter("Status", "الحالة", "Status", "select", null),
            },
            RecommendedMetricCodes: Array.Empty<string>(),
            RecommendedReportCodes: Array.Empty<string>(),
            RecommendedWidgetCodes: Array.Empty<string>(),
            Fields: new[]
            {
                new SemanticField("Loan","Status", "الحالة",  "Status", "حالة السلفة","Loan status","employment",null,new[]{"status","حالة"},  Dimension, true),
                new SemanticField("Loan","Amount", "المبلغ",  "Amount", "","","payroll",null,new[]{"amount","مبلغ"},                          Measure,   true),
            }),

        // ── Expense ───────────────────────────────────────────────────────────
        new SemanticObject(
            ObjectCode: "Expense", DomainCode: "expenses",
            NameAr: "المصروفات", NameEn: "Expenses",
            DescriptionAr: "مطالبات المصروفات", DescriptionEn: "Expense claims",
            Icon: "Receipt",
            Keywords: new[] { "expense","مصروف","مصروفات" },
            DefaultVisible: true,
            FieldGroups: new[]
            {
                new SemanticFieldGroup("employment", "التوظيف", "Employment", 2),
                new SemanticFieldGroup("payroll",    "الرواتب", "Payroll",    4),
            },
            DefaultSort: new SemanticSort("Amount", "Descending"),
            DefaultFilters: new[]
            {
                new SemanticFilter("Status", "الحالة", "Status", "select", null),
            },
            RecommendedMetricCodes: Array.Empty<string>(),
            RecommendedReportCodes: Array.Empty<string>(),
            RecommendedWidgetCodes: Array.Empty<string>(),
            Fields: new[]
            {
                new SemanticField("Expense","Status", "الحالة", "Status", "حالة المصروف","Expense status","employment",null,new[]{"status","حالة"},  Dimension, true),
                new SemanticField("Expense","Amount", "المبلغ", "Amount", "","","payroll",null,new[]{"amount","مبلغ"},                              Measure,   true),
            }),

        // ── EmployeeDocument ──────────────────────────────────────────────────
        new SemanticObject(
            ObjectCode: "EmployeeDocument", DomainCode: "documents",
            NameAr: "مستندات الموظفين", NameEn: "Employee Documents",
            DescriptionAr: "مستندات الموظفين والوثائق الرسمية", DescriptionEn: "Employee documents and official records",
            Icon: "FolderOpen",
            Keywords: new[] { "document","مستند","وثيقة","مستندات" },
            DefaultVisible: true,
            FieldGroups: new[]
            {
                new SemanticFieldGroup("documents", "المستندات", "Documents", 7),
                new SemanticFieldGroup("employment","التوظيف",   "Employment", 2),
            },
            DefaultSort: new SemanticSort("ExpiryDate", "Ascending"),
            DefaultFilters: new[]
            {
                new SemanticFilter("Type",       "النوع",       "Type",        "select",     null),
                new SemanticFilter("ExpiryDate", "تاريخ الانتهاء","Expiry Date","date-range", null),
            },
            RecommendedMetricCodes: new[] { "expiring_documents" },
            RecommendedReportCodes: Array.Empty<string>(),
            RecommendedWidgetCodes: Array.Empty<string>(),
            Fields: new[]
            {
                new SemanticField("EmployeeDocument","Type",       "النوع",             "Type",        "نوع المستند","Document type","documents",null,new[]{"type","نوع"},       Dimension, true),
                new SemanticField("EmployeeDocument","Title",      "العنوان",           "Title",       "","","documents",null,new[]{"title","عنوان"},                           Dimension, true),
                new SemanticField("EmployeeDocument","ExpiryDate", "تاريخ الانتهاء",   "Expiry Date", "","","documents",null,new[]{"expiry","انتهاء"},                         Dimension, true),
                new SemanticField("EmployeeDocument","IssueDate",  "تاريخ الإصدار",    "Issue Date",  "","","documents",null,new[]{"issue","اصدار"},                           Dimension, true),
            }),
    };

    public static readonly IReadOnlyList<SemanticMetric> Metrics = new[]
    {
        new SemanticMetric("total_employees","إجمالي الموظفين","Total Employees",
            "عدد جميع الموظفين","Count of all employees","Users","employees",
            new[]{"Employees.View"},
            new SemanticMetricDefinition("Employee","Count",null,Array.Empty<SemanticMetricFilter>(),null),
            "KpiCard", new[]{"DepartmentId","BranchId"}),

        new SemanticMetric("active_employees","الموظفون النشطون","Active Employees",
            "الموظفون بحالة نشط","Employees with Active status","UserCheck","employees",
            new[]{"Employees.View"},
            new SemanticMetricDefinition("Employee","Count","",
                new[]{ new SemanticMetricFilter("Status","Equals",Value:"1") }, null),
            "KpiCard", new[]{"DepartmentId","BranchId"}),

        new SemanticMetric("new_employees","التعيينات هذا الشهر","New Hires (This Month)",
            "الموظفون المعينون منذ بداية الشهر","Hired since start of month","UserPlus","employees",
            new[]{"Employees.View"},
            new SemanticMetricDefinition("Employee","Count","",
                new[]{ new SemanticMetricFilter("HireDate","GreaterThanOrEqual",RelativeValue:"startOfMonth") }, null),
            "KpiCard", new[]{"DepartmentId","BranchId"}),

        new SemanticMetric("employees_by_department","الموظفون حسب الإدارة","Employees by Department",
            "توزيع الموظفين على الإدارات","Employee distribution by department","BarChart3","employees",
            new[]{"Employees.View"},
            new SemanticMetricDefinition("Employee","Count",null,Array.Empty<SemanticMetricFilter>(),"DepartmentId"),
            "BarChart", new[]{"BranchId"}),

        new SemanticMetric("gross_payroll","إجمالي الاستحقاقات","Gross Payroll",
            "مجموع الاستحقاقات","Sum of gross earnings","Wallet","payroll",
            new[]{"Payroll.View"},
            new SemanticMetricDefinition("PayrollPayslip","Sum","GrossEarnings",Array.Empty<SemanticMetricFilter>(),null),
            "KpiCard", new[]{"DepartmentId","BranchId"}),

        new SemanticMetric("net_payroll","صافي الرواتب","Net Payroll",
            "مجموع صافي الرواتب","Sum of net amounts","Wallet","payroll",
            new[]{"Payroll.View"},
            new SemanticMetricDefinition("PayrollPayslip","Sum","NetAmount",Array.Empty<SemanticMetricFilter>(),null),
            "KpiCard", new[]{"DepartmentId","BranchId"}),

        new SemanticMetric("total_deductions","إجمالي الخصومات","Total Deductions",
            "مجموع الخصومات","Sum of deductions","Wallet","payroll",
            new[]{"Payroll.View"},
            new SemanticMetricDefinition("PayrollPayslip","Sum","TotalDeductions",Array.Empty<SemanticMetricFilter>(),null),
            "KpiCard", new[]{"DepartmentId"}),

        new SemanticMetric("late_employees","الموظفون المتأخرون","Late Employees",
            "عدد سجلات التأخير","Count of late attendance records","Clock","attendance",
            new[]{"Attendance.View"},
            new SemanticMetricDefinition("AttendanceRecord","Count","",
                new[]{ new SemanticMetricFilter("Status","Equals",Value:"6") }, null),
            "KpiCard", new[]{"DepartmentId","BranchId"}),

        new SemanticMetric("absent_employees","الموظفون الغائبون","Absent Employees",
            "عدد سجلات الغياب","Count of absent attendance records","UserX","attendance",
            new[]{"Attendance.View"},
            new SemanticMetricDefinition("AttendanceRecord","Count","",
                new[]{ new SemanticMetricFilter("Status","Equals",Value:"2") }, null),
            "KpiCard", new[]{"DepartmentId","BranchId"}),

        new SemanticMetric("overtime_minutes","إجمالي العمل الإضافي (دقائق)","Overtime (Minutes)",
            "مجموع دقائق العمل الإضافي","Sum of overtime minutes","Timer","attendance",
            new[]{"Attendance.View"},
            new SemanticMetricDefinition("AttendanceRecord","Sum","OvertimeMinutes",Array.Empty<SemanticMetricFilter>(),null),
            "KpiCard", new[]{"DepartmentId"}),

        new SemanticMetric("remaining_leave_balance","رصيد الإجازات المتبقي","Remaining Leave Balance",
            "إجمالي أرصدة الإجازات المتبقية","Total remaining leave days","CalendarCheck","leaves",
            new[]{"Leaves.View"},
            new SemanticMetricDefinition("LeaveBalance","Formula",null,Array.Empty<SemanticMetricFilter>(),null,
                Formula:"m1 + m2 - m3",
                Measures: new[]
                {
                    new SemanticMetricMeasure("m1","Sum","EntitledDays",       Array.Empty<SemanticMetricFilter>()),
                    new SemanticMetricMeasure("m2","Sum","CarriedForwardDays", Array.Empty<SemanticMetricFilter>()),
                    new SemanticMetricMeasure("m3","Sum","UsedDays",           Array.Empty<SemanticMetricFilter>()),
                }),
            "KpiCard", Array.Empty<string>()),

        new SemanticMetric("pending_requests","الطلبات المعلقة","Pending Requests",
            "الطلبات بحالة معلق","Requests with Pending status","Inbox","requests",
            new[]{"Requests.View"},
            new SemanticMetricDefinition("RequestInstance","Count","",
                new[]{ new SemanticMetricFilter("Status","Equals",Value:"1") }, null),
            "KpiCard", Array.Empty<string>()),

        new SemanticMetric("expiring_contracts","العقود المنتهية قريباً","Expiring Contracts",
            "العقود المنتهية خلال 30 يوماً","Contracts ending within 30 days","FileWarning","employees",
            new[]{"Employees.View"},
            new SemanticMetricDefinition("Employee","Count","",
                new[]
                {
                    new SemanticMetricFilter("ContractEndDate","GreaterThanOrEqual",RelativeValue:"today"),
                    new SemanticMetricFilter("ContractEndDate","LessThanOrEqual",   RelativeValue:"today+30d"),
                }, null),
            "KpiCard", new[]{"DepartmentId"}),

        new SemanticMetric("expiring_documents","المستندات المنتهية قريباً","Expiring Documents",
            "المستندات المنتهية خلال 30 يوماً","Documents expiring within 30 days","FileWarning","documents",
            new[]{"Employees.View"},
            new SemanticMetricDefinition("EmployeeDocument","Count","",
                new[]
                {
                    new SemanticMetricFilter("ExpiryDate","GreaterThanOrEqual",RelativeValue:"today"),
                    new SemanticMetricFilter("ExpiryDate","LessThanOrEqual",   RelativeValue:"today+30d"),
                }, null),
            "KpiCard", Array.Empty<string>()),

        // Intentionally self-hiding (no backing column / entity) — surface in health as known gaps:
        new SemanticMetric("total_gosi","إجمالي التأمينات","Total GOSI",
            "إجمالي اشتراكات التأمينات","Total GOSI contributions","ShieldCheck","payroll",
            new[]{"Payroll.View"},
            new SemanticMetricDefinition("PayrollPayslip","Sum","GosiAmount",Array.Empty<SemanticMetricFilter>(),null),
            "KpiCard", Array.Empty<string>()),

        new SemanticMetric("total_additions","إجمالي الإضافات","Total Additions",
            "إجمالي الإضافات","Total additions","PlusCircle","payroll",
            new[]{"Payroll.View"},
            new SemanticMetricDefinition("PayrollPayslip","Sum","TotalAdditions",Array.Empty<SemanticMetricFilter>(),null),
            "KpiCard", Array.Empty<string>()),

        new SemanticMetric("pending_approvals","الموافقات المعلقة","Pending Approvals",
            "الموافقات بانتظار القرار","Approvals awaiting decision","CheckSquare","requests",
            new[]{"Requests.View"},
            new SemanticMetricDefinition("RequestApproval","Count","",
                new[]{ new SemanticMetricFilter("Status","Equals",Value:"1") }, null),
            "KpiCard", Array.Empty<string>()),
    };

    // Search synonyms: normalized token -> expansion tokens (already Arabic-normalized where Arabic).
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Synonyms =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["راتب"]    = new[] { "payroll","salary","الرواتب" },
            ["payroll"] = new[] { "راتب","salary" },
            ["موظف"]    = new[] { "employee","staff" },
            ["employee"]= new[] { "موظف","staff" },
            ["تاخير"]   = new[] { "late","تأخير" },
            ["late"]    = new[] { "تاخير" },
            ["غياب"]    = new[] { "absent" },
            ["absent"]  = new[] { "غياب" },
            ["اجازه"]   = new[] { "leave","vacation","إجازة" },
            ["leave"]   = new[] { "اجازه","vacation" },
        };
}
