using System.Text;

namespace HR.Application.Engines.Finance.Export;

/// <summary>Plain-text writer supporting two shapes used by bank flat files: delimited (default tab) and
/// fixed-width (each cell right-padded/truncated to its column Width). Validation lives elsewhere — this
/// only serializes.</summary>
public sealed class TxtExportWriter : IExportWriter
{
    public ExportFormat Format => ExportFormat.Txt;
    public string ContentType => "text/plain";
    public string Extension => "txt";

    public byte[] Write(TabularDataset data, ExportWriteOptions? options = null)
    {
        var opt = options ?? new ExportWriteOptions();
        var sb = new StringBuilder();

        if (opt.IncludeHeader)
            sb.Append(Line(data.Columns.Select(c => c.Header).ToList(), data.Columns, opt)).Append("\r\n");

        foreach (var row in data.Rows)
        {
            var cells = data.Columns.Select(c => ExportValue.Format(row.TryGetValue(c.Key, out var v) ? v : null)).ToList();
            sb.Append(Line(cells, data.Columns, opt)).Append("\r\n");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Line(IReadOnlyList<string> cells, IReadOnlyList<TabularColumn> cols, ExportWriteOptions opt)
    {
        if (!opt.FixedWidth)
            return string.Join(opt.Delimiter, cells);

        var sb = new StringBuilder();
        for (int i = 0; i < cells.Count; i++)
        {
            var width = cols[i].Width ?? cells[i].Length;
            var cell = cells[i].Length > width ? cells[i][..width] : cells[i].PadRight(width);
            sb.Append(cell);
        }
        return sb.ToString();
    }
}
