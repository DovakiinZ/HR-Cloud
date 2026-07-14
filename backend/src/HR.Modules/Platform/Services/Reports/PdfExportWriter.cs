using HR.Application.Engines.Finance.Export;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Renders a <see cref="TabularDataset"/> into a landscape A4 PDF table via QuestPDF.
/// The QuestPDF Community license is set globally by <c>DocumentRenderer</c>'s static ctor.</summary>
public sealed class PdfExportWriter : IExportWriter
{
    static PdfExportWriter()
    {
        // Idempotent: guarantees the Community license is set even if DocumentRenderer's
        // static ctor hasn't run yet (e.g. an export before any document is rendered).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public ExportFormat Format => ExportFormat.Pdf;
    public string ContentType => "application/pdf";
    public string Extension => "pdf";

    public byte[] Write(TabularDataset data, ExportWriteOptions? options = null)
    {
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontSize(9));
                page.Header().Text(data.Title).FontSize(14).SemiBold();
                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        foreach (var _ in data.Columns) cols.RelativeColumn();
                    });
                    // header row
                    foreach (var c in data.Columns)
                        table.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text(c.Header).SemiBold();
                    // data rows
                    foreach (var row in data.Rows)
                        foreach (var c in data.Columns)
                        {
                            row.TryGetValue(c.Key, out var v);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3)
                                .Text(ExportValue.Format(v));
                        }
                });
                page.Footer().AlignRight().Text(x => { x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
            });
        });
        return doc.GeneratePdf();
    }
}
