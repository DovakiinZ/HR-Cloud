using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Forms;

public class FormField : BaseEntity
{
    public Guid FormDefinitionId { get; set; }
    public string Code { get; set; } = null!;
    public string NameEn { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public FieldType FieldType { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public string? SectionName { get; set; }
    public string? Placeholder { get; set; }
    public string? DefaultValue { get; set; }
    public string? ValidationRules { get; set; } // JSONB
    public string? Options { get; set; } // JSONB
    /// <summary>Governance metadata (JSONB): {"classification":"SystemRequired|BusinessRequired|Optional|Custom","isLocked":bool}. Null = Optional.</summary>
    public string? MetadataJson { get; set; }

    public FormDefinition FormDefinition { get; set; } = null!;
    public ICollection<FormSubmissionValue> SubmissionValues { get; set; } = new List<FormSubmissionValue>();
}
