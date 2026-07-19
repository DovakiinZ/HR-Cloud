using ClosedXML.Excel;
using HR.Application.Engines.Finance.Export;

namespace HR.Modules.Payroll.Export;

/// <summary>Excel (.xlsx) writer over the format-agnostic dataset, reusing the AttendanceExporter styling
/// (RTL, bold grey header, auto-fit). Numeric cells keep their type so Excel can sum/format them.
/// When <see cref="ExportWriteOptions.Branding"/> is non-null, a two-row company header block is
/// prepended (name row 1, meta row 2, spacer row 3) and the table starts at row 4.
/// When branding is null, behaviour is byte-identical to before (headers row 1, data row 2).</summary>
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

        int headerOffset = 0;
        var b = options?.Branding;
        if (b is not null)
        {
            // Row 1: company name (prefer Arabic)
            var name = string.IsNullOrWhiteSpace(b.NameAr) ? b.NameEn : b.NameAr;
            if (!string.IsNullOrWhiteSpace(name))
            {
                var nameCell = ws.Cell(1, 1);
                nameCell.Value = name;
                nameCell.Style.Font.Bold = true;
                nameCell.Style.Font.FontSize = 14;
            }

            // Row 2: CR / VAT / phone / email / address concatenated
            var metaParts = new[]
            {
                string.IsNullOrWhiteSpace(b.CommercialRegistration) ? null : $"س.ت: {b.CommercialRegistration}",
                string.IsNullOrWhiteSpace(b.VatNumber)              ? null : $"الرقم الضريبي: {b.VatNumber}",
                string.IsNullOrWhiteSpace(b.Phone)                  ? null : b.Phone,
                string.IsNullOrWhiteSpace(b.Email)                  ? null : b.Email,
                string.IsNullOrWhiteSpace(b.Address)                ? null : b.Address,
            }.Where(s => s is not null);
            var meta = string.Join("   |   ", metaParts);
            if (meta.Length > 0) ws.Cell(2, 1).Value = meta;

            // Rows 1-2 text + row 3 spacer; column headers start at row 4
            headerOffset = 3;

            // Logo: place near the last column (column count >= 1) so it doesn't overlap the name text.
            // Wrap in try/catch so a corrupt/unsupported image never breaks the export.
            if (b.LogoBytes is { Length: > 0 })
            {
                try
                {
                    using var img = new MemoryStream(b.LogoBytes);
                    ws.AddPicture(img)
                      .MoveTo(ws.Cell(1, Math.Max(1, data.Columns.Count)))
                      .WithSize(120, 48);
                }
                catch
                {
                    // Bad or unsupported logo format — skip silently.
                }
            }
        }

        // Column header row
        int headerRow = headerOffset + 1;
        for (int c = 0; c < data.Columns.Count; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = data.Columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        // Data rows
        for (int i = 0; i < data.Rows.Count; i++)
        {
            var row = data.Rows[i];
            for (int c = 0; c < data.Columns.Count; c++)
            {
                var v = row.TryGetValue(data.Columns[c].Key, out var val) ? val : null;
                var cell = ws.Cell(headerRow + 1 + i, c + 1);
                switch (v)
                {
                    case decimal dec: cell.Value = dec; break;
                    case double db:   cell.Value = db;  break;
                    case int ii:      cell.Value = ii;  break;
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
