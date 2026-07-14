using System.Collections.Generic;
using System.Linq;
using HR.Application.Engines.Finance.Export;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Pure projection of an executed ReportResult into the tabular export payload.
/// Grouped reports flatten depth-first: data rows, then a per-group subtotal row, then a grand-total row.</summary>
public static class ReportResultFlattener
{
    public static TabularDataset Flatten(ReportResult result, string title)
    {
        var columns = result.Columns
            .Select(c => new TabularColumn(c.Code, c.Label, c.IsMeasure ? TabularAlign.End : TabularAlign.Start))
            .ToList();

        var rows = new List<IReadOnlyDictionary<string, object?>>();

        if (result.Groups.Count > 0)
            foreach (var g in result.Groups) EmitGroup(g, result.Columns, rows);
        else
            foreach (var r in result.Rows) rows.Add(ProjectRow(r, result.Columns));

        if (result.GrandTotals.Count > 0)
            rows.Add(TotalRow("Grand Total", result.Columns, result.GrandTotals, result.Columns.FirstOrDefault()?.Code));

        return new TabularDataset(title, columns, rows);
    }

    private static void EmitGroup(ReportGroup g, List<ReportColumn> cols, List<IReadOnlyDictionary<string, object?>> rows)
    {
        if (g.SubGroups.Count > 0)
            foreach (var sub in g.SubGroups) EmitGroup(sub, cols, rows);
        else
            foreach (var r in g.Rows) rows.Add(ProjectRow(r, cols));

        // subtotal row keyed on the group's dimension column
        rows.Add(TotalRow($"{g.Label} — subtotal", cols, g.Aggregates, g.FieldCode));
    }

    private static IReadOnlyDictionary<string, object?> ProjectRow(ReportRow row, List<ReportColumn> cols)
    {
        var d = new Dictionary<string, object?>();
        foreach (var c in cols) d[c.Code] = row.TryGetValue(c.Code, out var v) ? v : null;
        return d;
    }

    private static IReadOnlyDictionary<string, object?> TotalRow(string label, List<ReportColumn> cols, IReadOnlyDictionary<string, double> aggregates, string? labelColumn)
    {
        var d = new Dictionary<string, object?>();
        foreach (var c in cols) d[c.Code] = null;
        if (labelColumn is not null && d.ContainsKey(labelColumn)) d[labelColumn] = label;
        foreach (var c in cols.Where(c => c.IsMeasure))
            if (aggregates.TryGetValue(c.Code, out var v)) d[c.Code] = v;
        return d;
    }
}
