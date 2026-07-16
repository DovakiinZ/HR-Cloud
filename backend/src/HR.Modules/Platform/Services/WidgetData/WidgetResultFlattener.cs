using System.Collections.Generic;
using System.Linq;
using HR.Application.Engines.Finance.Export;

namespace HR.Modules.Platform.Services.WidgetData;

/// <summary>Pure projection of an executed widget result into the tabular export payload.
/// scalar → one Value cell; series → Label/Value rows; table → the result's own columns/rows.</summary>
public static class WidgetResultFlattener
{
    private static readonly HashSet<string> NumericTypes = new(System.StringComparer.OrdinalIgnoreCase)
        { "Number", "Decimal", "Currency", "Percentage", "Int", "Integer", "Double", "Float", "Money" };

    public static TabularDataset Flatten(WidgetDataResult result, string title)
    {
        switch (result.Kind)
        {
            case "scalar":
            {
                var cols = new List<TabularColumn> { new("value", "Value", TabularAlign.End) };
                var rows = new List<IReadOnlyDictionary<string, object?>>
                    { new Dictionary<string, object?> { ["value"] = result.Value } };
                return new TabularDataset(title, cols, rows);
            }
            case "series":
            {
                var cols = new List<TabularColumn> { new("label", "Label"), new("value", "Value", TabularAlign.End) };
                var rows = result.Series
                    .Select(p => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["label"] = p.Label, ["value"] = p.Value })
                    .ToList();
                return new TabularDataset(title, cols, rows);
            }
            default: // table
            {
                var cols = result.Columns
                    .Select(c => new TabularColumn(c.Code, c.Label, NumericTypes.Contains(c.Type) ? TabularAlign.End : TabularAlign.Start))
                    .ToList();
                var rows = result.Rows
                    .Select(r =>
                    {
                        var d = new Dictionary<string, object?>();
                        foreach (var c in result.Columns) d[c.Code] = r.TryGetValue(c.Code, out var v) ? v : null;
                        return (IReadOnlyDictionary<string, object?>)d;
                    })
                    .ToList();
                return new TabularDataset(title, cols, rows);
            }
        }
    }
}
