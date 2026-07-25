namespace HR.Modules.Platform.DTOs.Forms;

public class FormDefinitionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string NameEn { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string? Description { get; set; }
    public string Module { get; set; } = null!;
    public int Version { get; set; }
    public bool IsPublished { get; set; }
    public bool IsActive { get; set; }
    public List<FormFieldDto> Fields { get; set; } = new();
}

public class FormFieldDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string NameEn { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string FieldType { get; set; } = null!;
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public string? SectionName { get; set; }
    public string? Placeholder { get; set; }
    public string? DefaultValue { get; set; }
    public string? ValidationRules { get; set; }
    public string? Options { get; set; }
    /// <summary>Read-only classification derived from MetadataJson. One of: SystemRequired, BusinessRequired, Optional, Custom.</summary>
    public string Classification { get; set; } = "Optional";
}

public class FormSubmissionDto
{
    public Guid Id { get; set; }
    public Guid FormDefinitionId { get; set; }
    public Guid SubmittedById { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string Status { get; set; } = null!;
    public List<FormSubmissionValueDto> Values { get; set; } = new();
}

public class FormSubmissionValueDto
{
    public Guid Id { get; set; }
    public Guid FormFieldId { get; set; }
    public string FieldCode { get; set; } = null!;
    public string? Value { get; set; }
    public string? FileUrl { get; set; }
}
