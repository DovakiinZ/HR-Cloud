using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Reports;

public class ReportField : BaseEntity
{
    public Guid ReportDefinitionId { get; set; }
    public ReportFieldType FieldType { get; set; }
    public Guid? ObjectDefinitionId { get; set; }
    public string FieldCode { get; set; } = null!;
    public string DisplayNameEn { get; set; } = null!;
    public string DisplayNameAr { get; set; } = null!;
    public AggregationType? Aggregation { get; set; }

    /// <summary>Serialized AST (AstJson) — what the execution path reads.</summary>
    public string? CalculationExpression { get; set; }

    /// <summary>The formula as authored. Stored alongside the AST because AST JSON is not
    /// round-trippable back into an editable formula box (mirrors Rule.ExpressionText).</summary>
    public string? CalculationText { get; set; }
    public string? FormatPattern { get; set; }
    public int Width { get; set; }
    public int SortOrder { get; set; }
    public bool IsVisible { get; set; } = true;

    public ReportDefinition ReportDefinition { get; set; } = null!;
}
