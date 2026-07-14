using System.Globalization;

namespace HR.Application.Engines.Finance.Export;

/// <summary>The output formats the export engine can render. A new format is a new IExportWriter; the
/// report providers and bank pipeline never change.</summary>
public enum ExportFormat { Excel = 1, Csv = 2, Txt = 3, Xml = 4, Pdf = 5 }

public enum TabularAlign { Start, End, Center }

/// <summary>One column of an export dataset. <paramref name="Key"/> matches the row dictionary key;
/// <paramref name="Width"/> drives fixed-width text output and Excel column sizing.</summary>
public sealed record TabularColumn(string Key, string Header, TabularAlign Align = TabularAlign.Start, int? Width = null);

/// <summary>A format-agnostic tabular dataset — the single shape every report and bank file projects into
/// before a writer serializes it. Rows are keyed by column Key so writers stay column-order-driven.</summary>
public sealed record TabularDataset(
    string Title,
    IReadOnlyList<TabularColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

public sealed record ExportWriteOptions(char Delimiter = '\t', bool FixedWidth = false, bool IncludeHeader = true);

/// <summary>Serializes a <see cref="TabularDataset"/> into one output format. DI-discovered by
/// <see cref="Format"/> so callers pick a writer without knowing concrete types.</summary>
public interface IExportWriter
{
    ExportFormat Format { get; }
    string ContentType { get; }
    string Extension { get; }
    byte[] Write(TabularDataset data, ExportWriteOptions? options = null);
}

/// <summary>Shared cell-value formatting so every writer stringifies values identically (invariant
/// culture; decimals without spurious trailing zeros).</summary>
public static class ExportValue
{
    public static string Format(object? v) => v switch
    {
        null => string.Empty,
        string s => s,
        decimal d => d.ToString("0.##", CultureInfo.InvariantCulture),
        double db => db.ToString("0.##", CultureInfo.InvariantCulture),
        System.DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? string.Empty,
    };
}
