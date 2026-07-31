using HR.Domain.Engines.MasterData;

namespace HR.Infrastructure.Persistence;

/// <summary>A single default master data row to seed for a new tenant.</summary>
public record MasterDataDefault(
    string ObjectType,
    string Code,
    string NameEn,
    string NameAr,
    string? Color = null,
    string? Icon = null,
    string? MetadataJson = null);

/// <summary>
/// Curated starter set of system-default master data, seeded per tenant on first
/// run (idempotently) and editable afterwards by tenant admins. Intentionally a
/// sensible starter — not exhaustive. Saudi-market oriented where relevant.
/// </summary>
public static class MasterDataDefaults
{
    public static IReadOnlyList<MasterDataDefault> All { get; } = Build();

    private static List<MasterDataDefault> Build()
    {
        var list = new List<MasterDataDefault>();

        void Add(string type, params (string Code, string En, string Ar)[] items)
        {
            foreach (var (code, en, ar) in items)
                list.Add(new MasterDataDefault(type, code, en, ar));
        }

        Add(MasterDataObjectType.EmploymentType,
            ("FULL_TIME", "Full Time", "دوام كامل"),
            ("PART_TIME", "Part Time", "دوام جزئي"),
            ("TEMPORARY", "Temporary", "مؤقت"),
            ("INTERN", "Intern", "متدرب"));

        Add(MasterDataObjectType.ContractType,
            ("PERMANENT", "Permanent", "دائم"),
            ("FIXED_TERM", "Fixed Term", "محدد المدة"),
            ("PROBATION", "Probation", "تحت التجربة"),
            ("CONSULTANT", "Consultant", "استشاري"));

        Add(MasterDataObjectType.LeaveType,
            ("ANNUAL", "Annual Leave", "إجازة سنوية"),
            ("SICK", "Sick Leave", "إجازة مرضية"),
            ("UNPAID", "Unpaid Leave", "إجازة بدون راتب"),
            ("MATERNITY", "Maternity Leave", "إجازة أمومة"),
            ("HAJJ", "Hajj Leave", "إجازة حج"),
            ("BEREAVEMENT", "Bereavement Leave", "إجازة وفاة"));

        list.Add(new MasterDataDefault(MasterDataObjectType.AttendancePermissionType, "GENERAL",         "General Permission",   "استئذان عام",   MetadataJson: "{\"paid\":true}"));
        list.Add(new MasterDataDefault(MasterDataObjectType.AttendancePermissionType, "EMERGENCY",       "Emergency Permission", "استئذان طارئ",  MetadataJson: "{\"paid\":true}"));
        list.Add(new MasterDataDefault(MasterDataObjectType.AttendancePermissionType, "PERSONAL",        "Personal Permission",  "استئذان شخصي",  MetadataJson: "{\"paid\":false}"));
        list.Add(new MasterDataDefault(MasterDataObjectType.AttendancePermissionType, "LATE_ARRIVAL",    "Late Arrival",         "تأخر صباحي",    MetadataJson: "{\"paid\":true}"));
        list.Add(new MasterDataDefault(MasterDataObjectType.AttendancePermissionType, "EARLY_DEPARTURE", "Early Departure",      "انصراف مبكر",   MetadataJson: "{\"paid\":true}"));

        Add(MasterDataObjectType.AllowanceType,
            ("HOUSING", "Housing Allowance", "بدل سكن"),
            ("TRANSPORT", "Transportation Allowance", "بدل نقل"),
            ("MOBILE", "Mobile Allowance", "بدل جوال"),
            ("FOOD", "Food Allowance", "بدل طعام"));

        Add(MasterDataObjectType.AssetType,
            ("LAPTOP", "Laptop", "حاسب محمول"),
            ("PHONE", "Mobile Phone", "هاتف"),
            ("SIM", "SIM Card", "شريحة اتصال"),
            ("DESK", "Desk & Chair", "مكتب وكرسي"),
            ("OTHER", "Other", "أخرى"));

        Add(MasterDataObjectType.AdditionType,
            ("BONUS", "Bonus", "مكافأة"),
            ("OVERTIME", "Overtime", "عمل إضافي"),
            ("COMMISSION", "Commission", "عمولة"),
            ("REWARD", "Reward", "حافز"));

        Add(MasterDataObjectType.DeductionType,
            ("GOSI", "GOSI", "التأمينات الاجتماعية"),
            ("LOAN", "Loan Repayment", "سداد قرض"),
            ("ABSENCE", "Absence Deduction", "خصم غياب"),
            ("PENALTY", "Penalty", "جزاء"),
            ("LATE", "Late Arrival Deduction", "خصم تأخير"),
            ("SHORTAGE", "Working Hours Shortage", "خصم نقص ساعات العمل"),
            ("UNPAID_PERMISSION", "Unpaid Permission Deduction", "خصم استئذان بدون أجر"));

        Add(MasterDataObjectType.DocumentType,
            ("CONTRACT", "Employment Contract", "عقد عمل"),
            ("ID_COPY", "National ID Copy", "صورة الهوية"),
            ("IQAMA", "Iqama", "إقامة"),
            ("PASSPORT", "Passport", "جواز سفر"),
            ("CERTIFICATE", "Certificate", "شهادة"),
            ("SALARY_LETTER", "Salary Letter", "خطاب راتب"));

        Add(MasterDataObjectType.RequestType,
            ("LEAVE", "Leave Request", "طلب إجازة"),
            ("LETTER", "Letter Request", "طلب خطاب"),
            ("EXPENSE", "Expense Claim", "مطالبة مصروف"),
            ("LOAN", "Loan Request", "طلب قرض"),
            ("BUSINESS_TRIP", "Business Trip", "رحلة عمل"));

        Add(MasterDataObjectType.RequestCategory,
            ("TIME_OFF", "Time Off & Attendance", "الإجازات والحضور"),
            ("FINANCE", "Finance & Payroll", "المالية والرواتب"),
            ("LETTERS", "Letters & Certificates", "الخطابات والشهادات"),
            ("IT", "IT & Equipment", "تقنية المعلومات والمعدات"),
            ("HR", "HR & Personal", "الموارد البشرية والشخصية"),
            ("GENERAL", "General", "عام"));

        Add(MasterDataObjectType.PaymentMethod,
            ("BANK_TRANSFER", "Bank Transfer", "تحويل بنكي"),
            ("CASH", "Cash", "نقدي"),
            ("SALARY_CARD", "Salary Card", "بطاقة راتب"));

        Add(MasterDataObjectType.PayrollGroup,
            ("MONTHLY", "Monthly Payroll", "رواتب شهرية"),
            ("HOURLY", "Hourly Payroll", "رواتب بالساعة"));

        Add(MasterDataObjectType.LeavePolicy,
            ("STANDARD", "Standard Policy", "السياسة القياسية"),
            ("EXECUTIVE", "Executive Policy", "سياسة تنفيذية"));

        Add(MasterDataObjectType.ShiftType,
            ("MORNING", "Morning Shift", "وردية صباحية"),
            ("EVENING", "Evening Shift", "وردية مسائية"),
            ("NIGHT", "Night Shift", "وردية ليلية"),
            ("FLEXIBLE", "Flexible", "مرن"));

        Add(MasterDataObjectType.ExpenseCategory,
            ("TRAVEL", "Travel", "سفر"),
            ("ACCOMMODATION", "Accommodation", "إقامة"),
            ("MEALS", "Meals", "وجبات"),
            ("OFFICE", "Office Supplies", "مستلزمات مكتبية"),
            ("TRAINING", "Training", "تدريب"));

        Add(MasterDataObjectType.LoanType,
            ("PERSONAL", "Personal Loan", "قرض شخصي"),
            ("EMERGENCY", "Emergency Loan", "قرض طارئ"),
            ("HOUSING", "Housing Loan", "قرض سكن"));

        Add(MasterDataObjectType.AssetType,
            ("LAPTOP", "Laptop", "حاسب محمول"),
            ("PHONE", "Mobile Phone", "هاتف جوال"),
            ("VEHICLE", "Vehicle", "مركبة"),
            ("ACCESS_CARD", "Access Card", "بطاقة دخول"));

        Add(MasterDataObjectType.RecruitmentSource,
            ("LINKEDIN", "LinkedIn", "لينكدإن"),
            ("REFERRAL", "Referral", "ترشيح"),
            ("WEBSITE", "Company Website", "موقع الشركة"),
            ("AGENCY", "Recruitment Agency", "وكالة توظيف"));

        Add(MasterDataObjectType.CandidateStage,
            ("APPLIED", "Applied", "تقدم"),
            ("SCREENING", "Screening", "فرز"),
            ("INTERVIEW", "Interview", "مقابلة"),
            ("OFFER", "Offer", "عرض"),
            ("HIRED", "Hired", "تم التوظيف"),
            ("REJECTED", "Rejected", "مرفوض"));

        Add(MasterDataObjectType.JobTitle,
            ("CEO", "Chief Executive Officer", "الرئيس التنفيذي"),
            ("HR_MANAGER", "HR Manager", "مدير الموارد البشرية"),
            ("ACCOUNTANT", "Accountant", "محاسب"),
            ("SWE", "Software Engineer", "مهندس برمجيات"),
            ("SALES_REP", "Sales Representative", "مندوب مبيعات"));

        Add(MasterDataObjectType.WorkLocation,
            ("HQ", "Head Office", "المكتب الرئيسي"),
            ("REMOTE", "Remote", "عن بُعد"),
            ("HYBRID", "Hybrid", "هجين"));

        Add(MasterDataObjectType.Bank,
            ("ALRAJHI", "Al Rajhi Bank", "مصرف الراجحي"),
            ("SNB", "Saudi National Bank", "البنك الأهلي السعودي"),
            ("RIYAD", "Riyad Bank", "بنك الرياض"),
            ("ALINMA", "Alinma Bank", "مصرف الإنماء"),
            ("SAB", "Saudi Awwal Bank", "البنك السعودي الأول"));

        Add(MasterDataObjectType.Nationality,
            ("SA", "Saudi", "سعودي"),
            ("EG", "Egyptian", "مصري"),
            ("IN", "Indian", "هندي"),
            ("PK", "Pakistani", "باكستاني"),
            ("PH", "Filipino", "فلبيني"),
            ("SD", "Sudanese", "سوداني"),
            ("YE", "Yemeni", "يمني"));

        Add(MasterDataObjectType.Tag,
            ("HIGH_POTENTIAL", "High Potential", "كفاءة واعدة"),
            ("KEY_TALENT", "Key Talent", "موهبة رئيسية"),
            ("NEW_HIRE", "New Hire", "تعيين جديد"));

        Add(MasterDataObjectType.Skill,
            ("LEADERSHIP", "Leadership", "قيادة"),
            ("COMMUNICATION", "Communication", "تواصل"),
            ("EXCEL", "Microsoft Excel", "مايكروسوفت إكسل"),
            ("PROJECT_MGMT", "Project Management", "إدارة المشاريع"));

        Add(MasterDataObjectType.Grade,
            ("G1", "Grade 1", "الدرجة 1"),
            ("G2", "Grade 2", "الدرجة 2"),
            ("G3", "Grade 3", "الدرجة 3"),
            ("G4", "Grade 4", "الدرجة 4"));

        // Payroll type categories (extensible). MetadataJson carries default export format + payment scope.
        list.Add(new MasterDataDefault(MasterDataObjectType.PayrollTypeCategory, "REGULAR",  "Regular Monthly",  "الرواتب الشهرية",      MetadataJson: "{\"defaultExportFormatCode\":\"PDF\"}"));
        list.Add(new MasterDataDefault(MasterDataObjectType.PayrollTypeCategory, "MUDAD",    "Mudad Payroll",    "رواتب مدد",             MetadataJson: "{\"defaultExportFormatCode\":\"MUDAD\"}"));
        list.Add(new MasterDataDefault(MasterDataObjectType.PayrollTypeCategory, "CASH",     "Cash Payroll",     "رواتب نقدية",           MetadataJson: "{\"defaultExportFormatCode\":\"CASH\"}"));
        list.Add(new MasterDataDefault(MasterDataObjectType.PayrollTypeCategory, "BONUS",    "Bonus Payroll",    "مسير المكافآت",         MetadataJson: "{\"defaultExportFormatCode\":\"PDF\"}"));
        list.Add(new MasterDataDefault(MasterDataObjectType.PayrollTypeCategory, "EOS",      "End of Service",   "مسير نهاية الخدمة",    MetadataJson: "{\"defaultExportFormatCode\":\"PDF\"}"));
        list.Add(new MasterDataDefault(MasterDataObjectType.PayrollTypeCategory, "OFFCYCLE", "Off-cycle",        "مسير استثنائي",         MetadataJson: "{\"defaultExportFormatCode\":\"PDF\"}"));

        // Export formats. MetadataJson.handlerKey maps to a code-registered exporter (sub-project 5).
        list.Add(new MasterDataDefault(MasterDataObjectType.PayrollExportFormat, "PDF",   "PDF",           "PDF",         MetadataJson: "{\"handlerKey\":\"pdf\"}"));
        list.Add(new MasterDataDefault(MasterDataObjectType.PayrollExportFormat, "EXCEL", "Excel",         "إكسل",        MetadataJson: "{\"handlerKey\":\"excel\"}"));
        list.Add(new MasterDataDefault(MasterDataObjectType.PayrollExportFormat, "CSV",   "CSV",           "CSV",         MetadataJson: "{\"handlerKey\":\"csv\"}"));
        list.Add(new MasterDataDefault(MasterDataObjectType.PayrollExportFormat, "TXT",   "Text",          "نص",          MetadataJson: "{\"handlerKey\":\"txt\"}"));
        list.Add(new MasterDataDefault(MasterDataObjectType.PayrollExportFormat, "BANK",  "Bank Transfer", "تحويل بنكي",  MetadataJson: "{\"handlerKey\":\"bank\"}"));
        list.Add(new MasterDataDefault(MasterDataObjectType.PayrollExportFormat, "MUDAD", "Mudad File",    "ملف مدد",     MetadataJson: "{\"handlerKey\":\"mudad\"}"));
        list.Add(new MasterDataDefault(MasterDataObjectType.PayrollExportFormat, "CASH",  "Cash Sheet",    "كشف نقدي",    MetadataJson: "{\"handlerKey\":\"cash\"}"));

        return list;
    }
}
