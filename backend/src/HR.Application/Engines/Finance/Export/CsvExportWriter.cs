using System.Text;

namespace HR.Application.Engines.Finance.Export;

/// <summary>RFC-4180 CSV writer with a UTF-8 BOM so Excel opens Arabic content correctly. Values
/// containing a comma, quote or newline are quoted; embedded quotes are doubled.</summary>
public sealed class CsvExportWriter : IExportWriter
{
    public ExportFormat Format => ExportFormat.Csv;
    public string ContentType => "text/csv";
    public string Extension => "csv";

    public byte[] Write(TabularDataset data, ExportWriteOptions? options = null)
    {
        var includeHeader = options?.IncludeHeader ?? true;
        var sb = new StringBuilder();

        if (includeHeader)
            sb.Append(string.Join(",", data.Columns.Select(c => Escape(c.Header)))).Append("\r\n");

        foreach (var row in data.Rows)
            sb.Append(string.Join(",", data.Columns.Select(c =>
                Escape(ExportValue.Format(row.TryGetValue(c.Key, out var v) ? v : null))))).Append("\r\n");

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Escape(string s) =>
        s.IndexOfAny(new[] { '"', ',', '\n', '\r' }) >= 0
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;
}
