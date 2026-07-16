namespace HR.Modules.Platform.DTOs.Reports;

public class ReportDefinitionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string NameEn { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string? Description { get; set; }
    public string ReportType { get; set; } = null!;
    public string Scope { get; set; } = null!;
    public Guid? OwnerId { get; set; }
    public Guid PrimaryObjectId { get; set; }
    public bool IsPublished { get; set; }
    public int Version { get; set; }
    public Guid? FolderId { get; set; }
    public List<ReportFieldDto> Fields { get; set; } = new();
    public List<ReportFilterDto> Filters { get; set; } = new();
    public List<ReportGroupingDto> Groupings { get; set; } = new();
    public List<ReportSortingDto> Sortings { get; set; } = new();
    public List<ReportRelationshipDto> Relationships { get; set; } = new();
    public List<ReportTagDto> Tags { get; set; } = new();

    /// <summary>Per-caller, not per-report: stitched in by ReportListProjector, never AutoMapper.</summary>
    public bool IsFavorite { get; set; }
    public bool IsPinned { get; set; }
}

public class ReportTemplateDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string NameEn { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string? Description { get; set; }
    public string ReportType { get; set; } = null!;
    public Guid PrimaryObjectId { get; set; }
    public string Configuration { get; set; } = null!;
    public bool IsSystem { get; set; }
}

public class ReportFieldDto
{
    public Guid Id { get; set; }
    public string FieldType { get; set; } = null!;
    public Guid? ObjectDefinitionId { get; set; }
    public string FieldCode { get; set; } = null!;
    public string DisplayNameEn { get; set; } = null!;
    public string DisplayNameAr { get; set; } = null!;
    public string? Aggregation { get; set; }
    public string? CalculationExpression { get; set; }
    public string? CalculationText { get; set; }
    public string? FormatPattern { get; set; }
    public int Width { get; set; }
    public int SortOrder { get; set; }
    public bool IsVisible { get; set; }
}

public class ReportFilterDto
{
    public Guid Id { get; set; }
    public string FieldCode { get; set; } = null!;
    public string Operator { get; set; } = null!;
    public string? Value { get; set; }
    public string? ValueTo { get; set; }
    public string? LogicalOperator { get; set; }
    public bool IsParameter { get; set; }
}

public class ReportGroupingDto
{
    public Guid Id { get; set; }
    public string FieldCode { get; set; } = null!;
    public int SortOrder { get; set; }
}

public class ReportSortingDto
{
    public Guid Id { get; set; }
    public string FieldCode { get; set; } = null!;
    public string Direction { get; set; } = null!;
    public int SortOrder { get; set; }
}

public class ReportScheduleDto
{
    public Guid Id { get; set; }
    public string Frequency { get; set; } = null!;
    public string? CronExpression { get; set; }
    public string ExportFormat { get; set; } = null!;
    public string Recipients { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
}

public class ReportShareDto
{
    public Guid Id { get; set; }
    public Guid ReportDefinitionId { get; set; }
    public Guid? SharedWithUserId { get; set; }
    public Guid? SharedWithDepartmentId { get; set; }
    public Guid? SharedWithRoleId { get; set; }
    public bool CanEdit { get; set; }
    public DateTime SharedAt { get; set; }
}

public class ReportRelationshipDto
{
    public Guid Id { get; set; }
    public Guid ReportDefinitionId { get; set; }
    public Guid SourceObjectId { get; set; }
    public Guid TargetObjectId { get; set; }
    public string JoinField { get; set; } = null!;
    public string JoinType { get; set; } = null!;
    public int SortOrder { get; set; }
}

public class RunReportRequest
{
    /// <summary>Values for filters marked IsParameter, keyed by field code (case-insensitive).
    /// A Between filter's upper bound is keyed "&lt;fieldCode&gt;:to".</summary>
    public Dictionary<string, string?>? Parameters { get; set; }
}

public class ValidateFormulaRequest
{
    public string? Formula { get; set; }
}

public class FormulaValidationDto
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
}
