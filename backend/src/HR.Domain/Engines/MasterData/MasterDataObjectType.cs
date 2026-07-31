namespace HR.Domain.Engines.MasterData;

/// <summary>
/// Canonical catalogue of master data object types. Each constant is the value
/// stored in <see cref="MasterDataItem.ObjectType"/>. <see cref="ToSlug"/> /
/// <see cref="FromSlug"/> convert between the canonical PascalCase name and the
/// kebab-case plural slug used in the lookup API (e.g. JobTitle &lt;-&gt; job-titles).
/// </summary>
public static class MasterDataObjectType
{
    public const string JobTitle = "JobTitle";
    public const string Department = "Department";
    public const string Branch = "Branch";
    public const string Position = "Position";
    public const string Grade = "Grade";
    public const string CostCenter = "CostCenter";
    public const string EmploymentType = "EmploymentType";
    public const string ContractType = "ContractType";
    public const string LeaveType = "LeaveType";
    public const string AttendancePermissionType = "AttendancePermissionType";
    public const string AllowanceType = "AllowanceType";
    public const string AdditionType = "AdditionType";
    public const string DeductionType = "DeductionType";
    public const string DocumentType = "DocumentType";
    public const string RequestType = "RequestType";
    public const string RequestCategory = "RequestCategory";
    public const string ShiftType = "ShiftType";
    public const string AttendancePolicy = "AttendancePolicy";
    public const string PayrollGroup = "PayrollGroup";
    public const string PaymentMethod = "PaymentMethod";
    public const string LeavePolicy = "LeavePolicy";
    public const string WorkLocation = "WorkLocation";
    public const string ExpenseCategory = "ExpenseCategory";
    public const string LoanType = "LoanType";
    public const string AssetType = "AssetType";
    public const string RecruitmentSource = "RecruitmentSource";
    public const string CandidateStage = "CandidateStage";
    public const string Tag = "Tag";
    public const string Skill = "Skill";
    public const string Bank = "Bank";
    public const string Nationality = "Nationality";
    public const string PayrollTypeCategory = "PayrollTypeCategory";
    public const string PayrollExportFormat = "PayrollExportFormat";

    /// <summary>All known canonical object types.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        JobTitle, Department, Branch, Position, Grade, CostCenter,
        EmploymentType, ContractType, LeaveType, AttendancePermissionType, AllowanceType, AdditionType, DeductionType,
        DocumentType, RequestType, RequestCategory, ShiftType, AttendancePolicy, PayrollGroup,
        PaymentMethod, LeavePolicy, WorkLocation, ExpenseCategory, LoanType, AssetType,
        RecruitmentSource, CandidateStage, Tag, Skill, Bank, Nationality,
        PayrollTypeCategory, PayrollExportFormat
    };

    private static readonly HashSet<string> Known =
        new(All, StringComparer.OrdinalIgnoreCase);

    public static bool IsValid(string? objectType) =>
        !string.IsNullOrWhiteSpace(objectType) && Known.Contains(objectType);

    /// <summary>Returns the canonical casing for a known type, or null when unknown.</summary>
    public static string? Normalize(string? objectType)
    {
        if (string.IsNullOrWhiteSpace(objectType)) return null;
        foreach (var t in All)
            if (string.Equals(t, objectType, StringComparison.OrdinalIgnoreCase))
                return t;
        return null;
    }

    /// <summary>JobTitle -&gt; "job-titles" (kebab-case, pluralised).</summary>
    public static string ToSlug(string objectType)
    {
        var kebab = string.Concat(objectType.Select((c, i) =>
            char.IsUpper(c) && i > 0 ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
        return Pluralize(kebab);
    }

    /// <summary>"job-titles" / "job-title" -&gt; JobTitle. Returns null when no known type matches.</summary>
    public static string? FromSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        var normalized = slug.Trim().ToLowerInvariant();
        foreach (var t in All)
        {
            var s = ToSlug(t);
            if (s == normalized || Singularize(s) == Singularize(normalized))
                return t;
        }
        return null;
    }

    private static string Pluralize(string word)
    {
        if (word.EndsWith("y") && word.Length > 1 && !"aeiou".Contains(word[^2]))
            return word[..^1] + "ies";
        if (word.EndsWith("s") || word.EndsWith("x") || word.EndsWith("ch") || word.EndsWith("sh"))
            return word + "es";
        return word + "s";
    }

    private static string Singularize(string word)
    {
        if (word.EndsWith("ies")) return word[..^3] + "y";
        if (word.EndsWith("es") &&
            (word.EndsWith("ses") || word.EndsWith("xes") || word.EndsWith("ches") || word.EndsWith("shes")))
            return word[..^2];
        if (word.EndsWith("s")) return word[..^1];
        return word;
    }
}
