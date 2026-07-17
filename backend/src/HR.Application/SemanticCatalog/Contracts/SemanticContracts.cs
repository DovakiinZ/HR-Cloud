namespace HR.Application.SemanticCatalog.Contracts;

public sealed record SemanticDomain(
    string Code, string NameAr, string NameEn, string DescriptionAr, string DescriptionEn,
    string Icon, int SortOrder);

public sealed record SemanticFieldGroup(string Code, string NameAr, string NameEn, int SortOrder);

public enum SemanticFieldRole { Dimension, Measure, Filter, Identifier }

public sealed record SemanticField(
    string ObjectCode, string FieldCode,
    string NameAr, string NameEn, string DescriptionAr, string DescriptionEn,
    string GroupCode, string? Icon, IReadOnlyList<string> Keywords,
    SemanticFieldRole Role, bool DefaultVisible);

public sealed record SemanticSort(string FieldCode, string Direction); // "Ascending"|"Descending"

public sealed record SemanticFilter(
    string FieldCode, string NameAr, string NameEn,
    string ControlType, string? ReferenceObjectCode); // control: select|date-range|search|reference

public sealed record SemanticObject(
    string ObjectCode, string DomainCode,
    string NameAr, string NameEn, string DescriptionAr, string DescriptionEn,
    string Icon, IReadOnlyList<string> Keywords, bool DefaultVisible,
    IReadOnlyList<SemanticFieldGroup> FieldGroups,
    SemanticSort? DefaultSort,
    IReadOnlyList<SemanticFilter> DefaultFilters,
    IReadOnlyList<string> RecommendedMetricCodes,
    IReadOnlyList<string> RecommendedReportCodes,
    IReadOnlyList<string> RecommendedWidgetCodes,
    IReadOnlyList<SemanticField> Fields);

public sealed record SemanticMetricFilter(
    string FieldCode, string Operator,
    string? Value = null, string? RelativeValue = null,
    string? ValueTo = null, string? RelativeValueTo = null);

public sealed record SemanticMetricMeasure(
    string Name, string Aggregation, string? AggregationField,
    IReadOnlyList<SemanticMetricFilter> Filters);

public sealed record SemanticMetricDefinition(
    string ObjectCode, string Aggregation, string? AggregationField,
    IReadOnlyList<SemanticMetricFilter> Filters, string? GroupByField,
    string? Formula = null, IReadOnlyList<SemanticMetricMeasure>? Measures = null);

public sealed record SemanticMetric(
    string Code, string NameAr, string NameEn, string DescriptionAr, string DescriptionEn,
    string Icon, string DomainCode, IReadOnlyList<string> RequiredPermissions,
    SemanticMetricDefinition Definition, string DefaultVisualization,
    IReadOnlyList<string> SuggestedFilterFields);

public sealed record HiddenItem(string Kind, string Code, string Reason); // Kind: Object|Field|Metric
public sealed record CatalogHealth(
    int VisibleObjects, int HiddenObjects, int VisibleMetrics, int HiddenMetrics,
    IReadOnlyList<HiddenItem> Hidden);
