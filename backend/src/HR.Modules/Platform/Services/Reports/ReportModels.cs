using HR.Domain.Enums;
using HR.Modules.Platform.Services.Catalog;

namespace HR.Modules.Platform.Services.Reports;

public sealed class ReportRow : Dictionary<string, object?>
{
    public ReportRow() : base(StringComparer.OrdinalIgnoreCase) { }
    public ReportRow(IDictionary<string, object?> src) : base(src, StringComparer.OrdinalIgnoreCase) { }
}

public sealed class ReportColumn
{
    public string Code { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string Type { get; set; } = "Text";
    public bool IsMeasure { get; set; }
    public AggregationType? Aggregation { get; set; }
    public string? FormatPattern { get; set; }
}

public sealed class ReportGroup
{
    public string FieldCode { get; set; } = null!;
    public object? Key { get; set; }
    public string Label { get; set; } = "";
    public List<ReportGroup> SubGroups { get; set; } = new();
    public List<ReportRow> Rows { get; set; } = new();
    public Dictionary<string, double> Aggregates { get; set; } = new();
    public long Count { get; set; }
}

public sealed class ReportResult
{
    public string ReportCode { get; set; } = null!;
    public List<ReportColumn> Columns { get; set; } = new();
    public List<ReportGroup> Groups { get; set; } = new();   // populated when the report has groupings
    public List<ReportRow> Rows { get; set; } = new();        // flat page when no groupings
    public Dictionary<string, double> GrandTotals { get; set; } = new();
    public long TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool Truncated { get; set; }
}

// ── Resolved plan (built by ReportObjectResolver, consumed by ReportSqlBuilder) ──

public sealed class ReportQueryModel
{
    public ResolvedObject Primary { get; set; } = null!;
    public string PrimaryAlias { get; set; } = "t0";
    public List<ReportJoinModel> Joins { get; set; } = new();
    public List<ReportColumnModel> Columns { get; set; } = new();
    public List<ReportFilterModel> Filters { get; set; } = new();
    public List<ReportSortModel> Sorts { get; set; } = new();
}

public sealed class ReportJoinModel
{
    public string Alias { get; set; } = null!;
    public ResolvedObject Target { get; set; } = null!;
    public string SourceAlias { get; set; } = null!;
    public string SourceColumn { get; set; } = null!;   // FK column on the source
    public string TargetKeyColumn { get; set; } = "Id";
    public string JoinType { get; set; } = "Inner";     // Inner|Left|Right
}

public sealed class ReportColumnModel
{
    public string TableAlias { get; set; } = null!;
    public ResolvedField Field { get; set; } = null!;
    public string OutputCode { get; set; } = null!;     // unique per SELECT item
}

public sealed class ReportFilterModel
{
    public string TableAlias { get; set; } = null!;
    public ResolvedField Field { get; set; } = null!;
    public ReportFilterOperator Operator { get; set; }
    public string? Value { get; set; }
    public string? ValueTo { get; set; }
}

public sealed class ReportSortModel
{
    public string TableAlias { get; set; } = null!;
    public ResolvedField Field { get; set; } = null!;
    public SortDirection Direction { get; set; }
}
