namespace HR.Application.Reports.Registry;

public sealed record ReportSubjectDescriptor(string Key, string LabelAr, string LabelEn, string Icon, int SortOrder);

public sealed record ReportJoinStep(string SourceObjectCode, string TargetObjectCode, string JoinField);

public sealed record ReportFieldDescriptor(
    string Key, string LabelAr, string LabelEn, string Subject, string Group, string DataType,
    Guid ObjectDefinitionId, string ObjectCode, string PropertyPath,
    IReadOnlyList<ReportJoinStep> JoinPath, IReadOnlyList<string> AllowedOperators,
    bool Filterable, bool Sortable, bool Groupable, bool Aggregatable, string? DefaultAggregation,
    bool IsDefault, int DisplayOrder, string? FormatPattern, string RequiredPermission);

public sealed record ReportResolveResult(
    IReadOnlyList<ReportFieldDescriptor> Fields,
    IReadOnlyList<ReportJoinStep> RequiredJoins,
    IReadOnlyList<string> UnknownKeys);

public sealed record ReportRegistryExclusion(string Key, string Reason);
public sealed record ReportRegistryHealth(
    int VisibleSubjects, int VisibleFields, int ExcludedFields,
    IReadOnlyList<ReportRegistryExclusion> Exclusions);

public sealed record ReportRegistryContext(IReadOnlyCollection<string> Permissions);
