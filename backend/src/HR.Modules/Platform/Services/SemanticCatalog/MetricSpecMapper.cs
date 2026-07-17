using HR.Application.SemanticCatalog.Contracts;
using HR.Modules.Platform.Services.WidgetData;

namespace HR.Modules.Platform.Services.SemanticCatalog;

/// <summary>Translates a validated SemanticMetricDefinition into the existing WidgetQuerySpec.
/// This is the ONLY place that knows about WidgetQuerySpec — it never leaks into the public contract.</summary>
public static class MetricSpecMapper
{
    private static readonly Dictionary<string, string> Ops = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Equals"] = "eq", ["NotEquals"] = "ne",
        ["GreaterThan"] = "gt", ["GreaterThanOrEqual"] = "gte",
        ["LessThan"] = "lt", ["LessThanOrEqual"] = "lte",
        ["Between"] = "between", ["In"] = "in", ["Contains"] = "contains",
    };

    public static WidgetQuerySpec ToWidgetSpec(SemanticMetricDefinition def, DateTime nowUtc) => new()
    {
        ObjectCode = def.ObjectCode,
        Aggregation = def.Aggregation,
        AggregationField = def.AggregationField,
        GroupByField = def.GroupByField,
        Formula = def.Formula,
        Filters = def.Filters.Select(f => MapFilter(f, nowUtc)).ToList(),
        Measures = def.Measures?.Select(m => new WidgetMeasureSpec
        {
            Name = m.Name, Aggregation = m.Aggregation, AggregationField = m.AggregationField,
            Filters = m.Filters.Select(f => MapFilter(f, nowUtc)).ToList(),
        }).ToList() ?? new(),
    };

    private static WidgetFilterSpec MapFilter(SemanticMetricFilter f, DateTime nowUtc) => new()
    {
        Field = f.FieldCode,
        Operator = Ops.TryGetValue(f.Operator, out var op) ? op : f.Operator,
        Value = ResolveValue(f.Value, f.RelativeValue, nowUtc),
    };

    private static string? ResolveValue(string? literal, string? relative, DateTime nowUtc)
        => relative is not null ? RelativeDate.Resolve(relative, nowUtc).ToString("yyyy-MM-dd") : literal;
}
