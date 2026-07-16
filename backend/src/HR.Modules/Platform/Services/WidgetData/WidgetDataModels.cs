namespace HR.Modules.Platform.Services.WidgetData;

// ──────────────────────────────────────────────────────────────────────────────
//  Query spec + result for the Widget Data engine. The spec is what a widget stores
//  in its Configuration JSONB and what the builder sends for live preview. It is
//  100% object-driven: it references objects/fields by canonical code only.
// ──────────────────────────────────────────────────────────────────────────────

public sealed class WidgetQuerySpec
{
    public string ObjectCode { get; set; } = null!;
    public string Aggregation { get; set; } = "Count";       // Count|Sum|Average|Min|Max|DistinctCount|Percentage
    public string? AggregationField { get; set; }
    public string? GroupByField { get; set; }
    public string? DateGranularity { get; set; }             // day|week|month|quarter|year (when grouping by a date)
    public string? Visualization { get; set; }               // informational
    public int? Limit { get; set; }
    public string? RequiredPermission { get; set; }          // optional gate (e.g. "Payroll.View")
    public List<WidgetFilterSpec> Filters { get; set; } = new();
    public string? Formula { get; set; }                    // used when Aggregation == "Formula"
    public List<WidgetMeasureSpec> Measures { get; set; } = new();
}

public sealed class WidgetFilterSpec
{
    public string Field { get; set; } = null!;
    public string Operator { get; set; } = "eq";            // eq|ne|gt|gte|lt|lte|contains|startsWith|in|between|last_n_days|is_null|not_null
    public string? Value { get; set; }
}

public sealed class WidgetMeasureSpec
{
    public string Name { get; set; } = null!;               // variable referenced by Formula (e.g. "m1")
    public string Aggregation { get; set; } = "Count";      // Count|Sum|Average|Min|Max|DistinctCount
    public string? AggregationField { get; set; }
    public List<WidgetFilterSpec> Filters { get; set; } = new();
}

public sealed class WidgetDataResult
{
    public string Kind { get; set; } = "scalar";            // scalar|series|table
    public string ObjectCode { get; set; } = null!;
    public string Aggregation { get; set; } = null!;
    public string? GroupByField { get; set; }

    // scalar
    public double? Value { get; set; }

    // series (grouped)
    public List<SeriesPoint> Series { get; set; } = new();

    // table / drilldown
    public List<TableColumn> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();

    public long TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public sealed class SeriesPoint
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public double Value { get; set; }
}

public sealed class TableColumn
{
    public string Code { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string Type { get; set; } = "Text";
}
