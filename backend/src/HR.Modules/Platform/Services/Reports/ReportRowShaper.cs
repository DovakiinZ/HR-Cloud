using HR.Domain.Enums;
using HR.Domain.Engines.Finance.Expressions;

namespace HR.Modules.Platform.Services.Reports;

public sealed class ComputedColumnSpec
{
    public string Code { get; set; } = null!;
    public Expr Ast { get; set; } = null!;
}

public sealed class ReportShapeSpec
{
    public string ReportCode { get; set; } = null!;
    public List<ReportColumn> Columns { get; set; } = new();
    public List<ComputedColumnSpec> Computed { get; set; } = new();
    public List<string> GroupByCodes { get; set; } = new();
    public List<(string Code, SortDirection Dir)> InMemorySorts { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public bool Truncated { get; set; }
}

/// <summary>Pure shaping: evaluates computed columns per row, applies in-memory sorts,
/// builds nested groups with measure aggregates + grand totals. No DB access.</summary>
public sealed class ReportRowShaper
{
    private readonly ComputedFieldEvaluator _evaluator;
    public ReportRowShaper(ComputedFieldEvaluator evaluator) => _evaluator = evaluator;

    public ReportResult Shape(IReadOnlyList<ReportRow> rows, ReportShapeSpec spec)
    {
        var working = rows.Select(r => new ReportRow(r)).ToList();

        // 1. Computed columns
        foreach (var row in working)
            foreach (var c in spec.Computed)
            {
                try { row[c.Code] = _evaluator.Evaluate(c.Ast, row); }
                catch (HR.Domain.Engines.Finance.Expressions.ExpressionException) { row[c.Code] = null; }
            }

        // 2. In-memory sorts (needed for computed-field sorts; harmless for object fields)
        IEnumerable<ReportRow> sorted = working;
        foreach (var s in Enumerable.Reverse(spec.InMemorySorts))
            sorted = s.Dir == SortDirection.Descending
                ? sorted.OrderByDescending(r => r.GetValueOrDefault(s.Code))
                : sorted.OrderBy(r => r.GetValueOrDefault(s.Code));
        working = sorted.ToList();

        var measures = spec.Columns.Where(c => c.IsMeasure && c.Aggregation is not null).ToList();
        var result = new ReportResult
        {
            ReportCode = spec.ReportCode, Columns = spec.Columns,
            TotalCount = working.Count, Page = spec.Page, PageSize = spec.PageSize, Truncated = spec.Truncated,
            GrandTotals = Aggregate(working, measures),
        };

        if (spec.GroupByCodes.Count == 0)
        {
            result.Rows = working.Skip((spec.Page - 1) * spec.PageSize).Take(spec.PageSize).ToList();
            return result;
        }

        result.Groups = BuildGroups(working, spec.GroupByCodes, 0, measures);
        return result;
    }

    private List<ReportGroup> BuildGroups(List<ReportRow> rows, List<string> groupCodes, int level, List<ReportColumn> measures)
    {
        var code = groupCodes[level];
        var groups = new List<ReportGroup>();
        foreach (var g in rows.GroupBy(r => r.GetValueOrDefault(code)))
        {
            var members = g.ToList();
            var group = new ReportGroup
            {
                FieldCode = code, Key = g.Key, Label = g.Key?.ToString() ?? "—",
                Count = members.Count, Aggregates = Aggregate(members, measures),
            };
            if (level + 1 < groupCodes.Count)
                group.SubGroups = BuildGroups(members, groupCodes, level + 1, measures);
            else
                group.Rows = members;
            groups.Add(group);
        }
        return groups;
    }

    private static Dictionary<string, double> Aggregate(List<ReportRow> rows, List<ReportColumn> measures)
    {
        var totals = new Dictionary<string, double>();
        foreach (var m in measures)
        {
            var nums = rows.Select(r => r.GetValueOrDefault(m.Code)).Where(v => v is not null)
                           .Select(v => System.Convert.ToDouble(v)).ToList();
            totals[m.Code] = m.Aggregation switch
            {
                AggregationType.Sum => nums.Sum(),
                AggregationType.Average => nums.Count > 0 ? nums.Average() : 0,
                AggregationType.Min => nums.Count > 0 ? nums.Min() : 0,
                AggregationType.Max => nums.Count > 0 ? nums.Max() : 0,
                AggregationType.Count => rows.Count,
                _ => nums.Sum(),
            };
        }
        return totals;
    }
}
