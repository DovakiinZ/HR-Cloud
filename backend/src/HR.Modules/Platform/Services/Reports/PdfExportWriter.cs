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
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Fonts");
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.ttf"))
                {
                    using var fs = File.OpenRead(f);
                    QuestPDF.Drawing.FontManager.RegisterFont(fs);
                }
        }
        catch { /* fall back to system fonts */ }
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
                page.DefaultTextStyle(x => x.FontFamily("Tajawal").FontSize(9).DirectionFromRightToLeft());
                page.Header().Element(h => ComposeHeader(h, data.Title, options?.Branding));
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

    private static void ComposeHeader(IContainer container, string title, CompanyBranding? b)
    {
        container.Column(col =>
        {
            if (b is not null)
            {
                col.Item().Row(row =>
                {
                    if (b.LogoBytes is { Length: > 0 })
                    {
                        try { row.ConstantItem(90).Height(48).Image(b.LogoBytes).FitArea(); }
                        catch { /* bad logo bytes -> skip */ }
                    }
                    row.RelativeItem().Column(info =>
                    {
                        var name = string.IsNullOrWhiteSpace(b.NameAr) ? b.NameEn : b.NameAr;
                        if (!string.IsNullOrWhiteSpace(name)) info.Item().Text(name).FontSize(14).SemiBold();
                        var line2 = string.Join("  •  ", new[]
                        {
                            string.IsNullOrWhiteSpace(b.CommercialRegistration) ? null : $"س.ت: {b.CommercialRegistration}",
                            string.IsNullOrWhiteSpace(b.VatNumber) ? null : $"الرقم الضريبي: {b.VatNumber}",
                            string.IsNullOrWhiteSpace(b.Phone) ? null : b.Phone,
                            string.IsNullOrWhiteSpace(b.Email) ? null : b.Email,
                        }.Where(s => s is not null));
                        if (line2.Length > 0) info.Item().Text(line2).FontSize(8).FontColor(Colors.Grey.Darken1);
                        if (!string.IsNullOrWhiteSpace(b.Address)) info.Item().Text(b.Address!).FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
                col.Item().PaddingTop(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
            }
            col.Item().PaddingTop(6).Text(title).FontSize(13).SemiBold();
        });
    }
}
