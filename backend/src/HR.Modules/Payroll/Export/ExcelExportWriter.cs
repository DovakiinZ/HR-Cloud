using ClosedXML.Excel;
using HR.Application.Engines.Finance.Export;

namespace HR.Modules.Payroll.Export;

/// <summary>Excel (.xlsx) writer over the format-agnostic dataset, reusing the AttendanceExporter styling
/// (RTL, bold grey header, auto-fit). Numeric cells keep their type so Excel can sum/format them.</summary>
public sealed class ExcelExportWriter : IExportWriter
{
    public ExportFormat Format => ExportFormat.Excel;
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string Extension => "xlsx";

    public byte[] Write(TabularDataset data, ExportWriteOptions? options = null)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName(data.Title));
        ws.RightToLeft = true;

        for (int c = 0; c < data.Columns.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = data.Columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        for (int i = 0; i < data.Rows.Count; i++)
        {
            var row = data.Rows[i];
            for (int c = 0; c < data.Columns.Count; c++)
            {
                var v = row.TryGetValue(data.Columns[c].Key, out var val) ? val : null;
                var cell = ws.Cell(i + 2, c + 1);
                switch (v)
                {
                    case decimal dec: cell.Value = dec; break;
                    case double db: cell.Value = db; break;
                    case int ii: cell.Value = ii; break;
                    case System.DateTime dt: cell.Value = dt; break;
                    default: cell.Value = ExportValue.Format(v); break;
                }
            }
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string SheetName(string title)
    {
        var t = string.IsNullOrWhiteSpace(title) ? "Sheet1" : title;
        foreach (var bad in new[] { '\\', '/', '*', '?', ':', '[', ']' }) t = t.Replace(bad, ' ');
        return t.Length > 31 ? t[..31] : t;
    }
}
