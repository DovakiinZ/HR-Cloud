using System.Text.Json;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HR.Modules.Platform.Services.Documents;

/// <summary>A render job decoupled from any single entity: a template (optional), a title/ref, the
/// resolved token dictionary, optional default detail rows (used when the template has no body), and
/// an optional approval chain. Used for request PDFs and leave-record PDFs alike.</summary>
public sealed record DocumentRenderRequest(
    Guid? TemplateId,
    string FallbackTitle,
    string RefNumber,
    IReadOnlyDictionary<string, string> Tokens,
    IReadOnlyList<(string Label, string Value)>? DefaultDetails,
    IReadOnlyList<(int Order, string Name, string Status)>? Approvals,
    string FileName,
    // Optional payslip breakdown. When present, a template's "components" block renders it as a dynamic
    // earnings/deductions/net table — the channel that lets one template serve any component set. Reuses
    // the tested Domain projection (totals included); PayslipLabels maps ComponentCode → display label.
    HR.Domain.Engines.Finance.Payslips.PayslipBreakdown? Payslip = null,
    IReadOnlyDictionary<string, string>? PayslipLabels = null);

/// <summary>Renders official PDFs using QuestPDF — works on Linux, no native deps. New templates use
/// the JSON block model (<c>LayoutJson</c>) composed with a PageTemplate's chrome
/// (header/footer/margins/watermark); legacy HTML bodies still render.</summary>
public interface IDocumentRenderer
{
    Task<(byte[] pdf, string fileName)> RenderRequestPdfAsync(Guid requestInstanceId, CancellationToken ct);
    Task<(byte[] pdf, string fileName)> RenderRequestPdfAsync(Guid requestInstanceId, Guid? templateId, CancellationToken ct);
    Task<(byte[] pdf, string fileName)> RenderDocumentAsync(DocumentRenderRequest req, CancellationToken ct);
}

public sealed class DocumentRenderer : IDocumentRenderer
{
    private readonly ApplicationDbContext _db;
    private readonly IDocumentTokenResolver _tokens;
    // Tajawal: a modern Arabic+Latin sans that renders reliably under Skia on Linux. The brand
    // font (Thmanyah Sans) is OTF/CFF and Skia-on-Linux fails to render its Arabic glyphs (".notdef"
    // boxes), so it can't be used for server-side PDF generation.
    private const string FontFamily = "Tajawal";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    static DocumentRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Fonts");
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.ttf"))
                    QuestPDF.Drawing.FontManager.RegisterFont(File.OpenRead(f));
        }
        catch { /* fall back to system fonts */ }
    }

    public DocumentRenderer(ApplicationDbContext db, IDocumentTokenResolver tokens) { _db = db; _tokens = tokens; }

    // ── Request path (builds a render job from the request, then delegates) ──

    public Task<(byte[] pdf, string fileName)> RenderRequestPdfAsync(Guid requestInstanceId, CancellationToken ct)
        => RenderRequestPdfAsync(requestInstanceId, null, ct);

    public async Task<(byte[] pdf, string fileName)> RenderRequestPdfAsync(Guid requestInstanceId, Guid? templateId, CancellationToken ct)
    {
        var instance = await _db.RequestInstances.Include(r => r.RequestType)
            .FirstOrDefaultAsync(r => r.Id == requestInstanceId, ct)
            ?? throw new HR.Application.Common.Exceptions.NotFoundException("RequestInstance", requestInstanceId);

        var approvals = await _db.RequestApprovals.Where(a => a.RequestInstanceId == requestInstanceId)
            .OrderBy(a => a.StepOrder)
            .Select(a => new { a.StepOrder, a.StepNameAr, Status = a.Status })
            .ToListAsync(ct);

        var tokenValues = await _tokens.ResolveForRequestAsync(requestInstanceId, ct);
        var tplId = templateId ?? instance.RequestType.PrintTemplateId;

        var req = new DocumentRenderRequest(
            TemplateId: tplId,
            FallbackTitle: instance.RequestType.NameAr,
            RefNumber: instance.RequestNumber,
            Tokens: tokenValues,
            DefaultDetails: null, // → generic Employee/Leave detail table
            Approvals: approvals.Select(a => (a.StepOrder, a.StepNameAr, StatusAr(a.Status.ToString()))).ToList(),
            FileName: $"{instance.RequestType.Code}-{instance.RequestNumber}.pdf");

        return await RenderDocumentAsync(req, ct);
    }

    // ── Generic core ──

    public async Task<(byte[] pdf, string fileName)> RenderDocumentAsync(DocumentRenderRequest req, CancellationToken ct)
    {
        var company = await _db.CompanyProfiles.FirstOrDefaultAsync(ct);

        HR.Domain.Engines.Documents.DocumentTemplate? tpl = null;
        if (req.TemplateId is { } id)
            tpl = await _db.DocumentTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);

        var title = tpl?.NameAr ?? req.FallbackTitle;

        HR.Domain.Engines.Documents.PageTemplate? page = null;
        if (tpl?.PageTemplateId is { } pid)
            page = await _db.PageTemplates.FirstOrDefaultAsync(p => p.Id == pid, ct);

        var header = ParseCfg(page?.HeaderConfig);
        var footer = ParseCfg(page?.FooterConfig);
        var margins = ParseCfg(page?.Margins);
        var watermark = ParseCfg(page?.Watermark);

        var logo = await LoadImageAsync(company?.LogoUrl, ct);
        var stamp = await LoadImageAsync(company?.StampUrl, ct);
        var hrSig = await LoadImageAsync(company?.HrSignatureUrl, ct);
        var ceoSig = await LoadImageAsync(company?.CeoSignatureUrl, ct);
        var contact = ParseJson(company?.ContactInfo);
        var address = ParseJson(company?.NationalAddress);

        var tokenValues = req.Tokens;
        var employeeName = tokenValues.GetValueOrDefault("Employee.FullName", "—");
        var generatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var qr = MakeQr($"{company?.NameAr} | {req.RefNumber} | {employeeName} | {generatedDate}");

        List<Block>? blocks = null;
        if (!string.IsNullOrWhiteSpace(tpl?.LayoutJson))
        {
            try { blocks = JsonSerializer.Deserialize<Layout>(tpl!.LayoutJson!, JsonOpts)?.Blocks; }
            catch { blocks = null; }
        }
        var legacyHtml = blocks is null && !string.IsNullOrWhiteSpace(tpl?.BodyTemplate)
            ? ParseHtmlBlocks(ResolveTokens(tpl!.BodyTemplate!, tokenValues)) : null;

        var blockImages = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (blocks is not null)
            foreach (var b in blocks.Where(b => string.Equals(b.Type, "image", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(b.FileId)))
                if (!blockImages.ContainsKey(b.FileId!) && await LoadImageAsync(b.FileId, ct) is { } bytes)
                    blockImages[b.FileId!] = bytes;

        // Default detail table: caller-supplied rows, else the generic Employee/Leave fields.
        var details = req.DefaultDetails?.Select(d => (d.Label, d.Value)).ToList()
            ?? BuildGenericDetails(tokenValues);

        var approvals = req.Approvals ?? Array.Empty<(int, string, string)>();
        var refNumber = req.RefNumber;

        byte[] BuildPdf(bool useBody) => Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.MarginTop(GetNum(margins, "top", 36));
                p.MarginBottom(GetNum(margins, "bottom", 36));
                p.MarginLeft(GetNum(margins, "left", 36));
                p.MarginRight(GetNum(margins, "right", 36));
                p.DefaultTextStyle(x => x.FontFamily(FontFamily).FontSize(11).DirectionFromRightToLeft());

                var wmText = GetStr(watermark, "text");
                if (!string.IsNullOrWhiteSpace(wmText))
                    p.Background().AlignCenter().AlignMiddle().Text(wmText!).FontSize(60).FontColor(Colors.Grey.Lighten3);

                if (GetBool(header, "showLogo", true) || GetBool(header, "showIdentity", true) || !string.IsNullOrWhiteSpace(GetStr(header, "customText")))
                {
                    p.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            var placement = GetStr(header, "logoPlacement") ?? "Left";
                            void Identity()
                            {
                                row.RelativeItem().Column(c =>
                                {
                                    if (GetBool(header, "showIdentity", true))
                                    {
                                        c.Item().Text(company?.NameAr ?? "الشركة").FontSize(16).Bold();
                                        if (!string.IsNullOrWhiteSpace(company?.NameEn)) c.Item().Text(company!.NameEn).FontSize(10).FontColor(Colors.Grey.Medium);
                                    }
                                    if (GetBool(header, "showCrVat", true))
                                    {
                                        var line = string.Join("  •  ", new[]
                                        {
                                            company?.CommercialRegistration is { Length: > 0 } cr ? $"س.ت: {cr}" : null,
                                            company?.VatNumber is { Length: > 0 } vat ? $"ض.ق.م: {vat}" : null,
                                        }.Where(s => s is not null));
                                        if (line.Length > 0) c.Item().Text(line).FontSize(9).FontColor(Colors.Grey.Medium);
                                    }
                                    if (GetBool(header, "showContact", true))
                                    {
                                        var contactLine = string.Join("  •  ", new[]
                                        {
                                            company?.Phone ?? Str(contact, "phone"),
                                            company?.Email ?? Str(contact, "email"),
                                            company?.Website ?? Str(contact, "website"),
                                            string.Join(" ", new[] { company?.Address, company?.City, company?.Country }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } addr ? addr : Str(address, "city"),
                                        }.Where(s => !string.IsNullOrWhiteSpace(s)));
                                        if (contactLine.Length > 0) c.Item().Text(contactLine).FontSize(9).FontColor(Colors.Grey.Medium);
                                    }
                                    if (GetStr(header, "customText") is { Length: > 0 } htxt) c.Item().Text(htxt).FontSize(9).FontColor(Colors.Grey.Medium);
                                });
                            }
                            void Logo() { if (logo is not null && GetBool(header, "showLogo", true)) row.ConstantItem(90).Height(60).Image(logo).FitArea(); }

                            if (placement.Equals("Right", StringComparison.OrdinalIgnoreCase)) { Logo(); Identity(); }
                            else { Identity(); Logo(); }
                        });
                        col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                    });
                }

                p.Content().PaddingVertical(16).Column(col =>
                {
                    col.Spacing(12);
                    col.Item().AlignCenter().Text(title).FontSize(15).Bold();

                    if (useBody && blocks is not null)
                        RenderBlocks(col, blocks, tokenValues, qr, stamp, hrSig, ceoSig, blockImages, req.Payslip, req.PayslipLabels);
                    else if (useBody && legacyHtml is not null)
                        RenderLegacy(col, legacyHtml);
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(refNumber))
                            col.Item().Text($"المرجع: {refNumber}").FontSize(10).FontColor(Colors.Grey.Medium);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); });
                            foreach (var (k, v) in details)
                            {
                                table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text(k).Bold().FontSize(10);
                                table.Cell().Padding(6).Text(v).FontSize(10);
                            }
                        });
                    }

                    if (approvals.Count > 0)
                    {
                        col.Item().PaddingTop(6).Text("سلسلة الموافقات").Bold();
                        col.Item().Column(ac =>
                        {
                            foreach (var (order, name, status) in approvals)
                                ac.Item().Text($"{order}. {name} — {status}").FontSize(10);
                        });
                    }
                });

                p.Footer().Column(col =>
                {
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                    col.Item().PaddingTop(6).Row(row =>
                    {
                        if (GetBool(footer, "showQr", true)) row.ConstantItem(70).Height(70).Image(qr).FitArea();
                        var mid = GetStr(footer, "customText");
                        if (GetBool(footer, "showGeneratedDate", true))
                            row.RelativeItem().AlignMiddle().Text($"تاريخ الإصدار: {generatedDate}{(string.IsNullOrWhiteSpace(mid) ? "" : "  •  " + mid)}").FontSize(9).FontColor(Colors.Grey.Medium);
                        else if (!string.IsNullOrWhiteSpace(mid))
                            row.RelativeItem().AlignMiddle().Text(mid!).FontSize(9).FontColor(Colors.Grey.Medium);
                        if (stamp is not null && GetBool(footer, "showStamp", true)) row.ConstantItem(80).Height(70).AlignLeft().Image(stamp).FitArea();
                    });
                });
            });
        }).GeneratePdf();

        byte[] pdf;
        try { pdf = BuildPdf(true); }
        catch (QuestPDF.Drawing.Exceptions.DocumentLayoutException ex)
        {
            Console.Error.WriteLine($"[DocumentRenderer] body layout failed, falling back: {ex.Message}");
            pdf = BuildPdf(false);
        }

        return (pdf, req.FileName);
    }

    private static List<(string, string)> BuildGenericDetails(IReadOnlyDictionary<string, string> t)
    {
        var details = new List<(string, string)>
        {
            ("اسم الموظف", t.GetValueOrDefault("Employee.FullName", "—")),
            ("الرقم الوظيفي", t.GetValueOrDefault("Employee.EmployeeNumber", "—")),
            ("الإدارة", t.GetValueOrDefault("Employee.Department", "—")),
            ("المسمى الوظيفي", t.GetValueOrDefault("Employee.JobTitle", "—")),
        };
        if (!string.IsNullOrEmpty(t.GetValueOrDefault("Leave.Type"))) details.Add(("نوع الإجازة", t["Leave.Type"]));
        if (!string.IsNullOrEmpty(t.GetValueOrDefault("Leave.StartDate"))) details.Add(("من", t["Leave.StartDate"]));
        if (!string.IsNullOrEmpty(t.GetValueOrDefault("Leave.EndDate"))) details.Add(("إلى", t["Leave.EndDate"]));
        if (!string.IsNullOrEmpty(t.GetValueOrDefault("Leave.Days"))) details.Add(("عدد الأيام", t["Leave.Days"]));
        return details;
    }

    // ── Block model rendering ──

    private static void RenderBlocks(QuestPDF.Fluent.ColumnDescriptor col, List<Block> blocks, IReadOnlyDictionary<string, string> tokens,
        byte[] qr, byte[]? stamp, byte[]? hrSig, byte[]? ceoSig, IReadOnlyDictionary<string, byte[]> blockImages,
        HR.Domain.Engines.Finance.Payslips.PayslipBreakdown? payslip = null,
        IReadOnlyDictionary<string, string>? payslipLabels = null)
    {
        foreach (var b in blocks)
        {
            var kind = (b.Type ?? "text").ToLowerInvariant();
            switch (kind)
            {
                case "title":
                    Aligned(col, b.Align, "center").Text(ResolveTokens(b.Text ?? "", tokens)).FontSize(SizePt(b.Size, 15)).Bold();
                    break;
                case "text":
                {
                    var span = Aligned(col, b.Align, "right").Text(ResolveTokens(b.Text ?? "", tokens)).FontSize(SizePt(b.Size, 11));
                    if (b.Bold) span.Bold();
                    break;
                }
                case "token":
                {
                    var val = tokens.TryGetValue(StripBraces(b.Token), out var tv) ? tv : (b.Text ?? "");
                    var span = Aligned(col, b.Align, "right").Text(val).FontSize(SizePt(b.Size, 11));
                    if (b.Bold) span.Bold();
                    break;
                }
                case "table":
                    if (b.Rows is { Count: > 0 })
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); });
                            foreach (var r in b.Rows)
                            {
                                table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text(ResolveTokens(r.Label ?? "", tokens)).Bold().FontSize(10);
                                table.Cell().Padding(6).Text(ResolveTokens(r.Value ?? "", tokens)).FontSize(10);
                            }
                        });
                    break;
                case "image":
                    if (b.FileId is { } fid && blockImages.TryGetValue(fid, out var img))
                        Aligned(col, b.Align, "center").Width(b.Width ?? 160).Image(img).FitWidth();
                    break;
                case "qr":
                    Aligned(col, b.Align, "center").Width(b.Width ?? 90).Image(qr).FitWidth();
                    break;
                case "signature":
                    var sig = string.Equals(b.Role, "ceo", StringComparison.OrdinalIgnoreCase) ? ceoSig : hrSig;
                    col.Item().PaddingTop(10).AlignCenter().Column(c =>
                    {
                        if (sig is not null) c.Item().Width(b.Width ?? 140).Image(sig).FitWidth();
                        c.Item().Text(b.Label ?? (string.Equals(b.Role, "ceo", StringComparison.OrdinalIgnoreCase) ? "الرئيس التنفيذي" : "إدارة الموارد البشرية")).FontSize(10).Bold();
                    });
                    break;
                case "stamp":
                    if (stamp is not null) Aligned(col, b.Align, "left").Width(b.Width ?? 100).Image(stamp).FitWidth();
                    break;
                case "components":
                    if (payslip is not null && (payslip.Earnings.Count > 0 || payslip.Deductions.Count > 0))
                    {
                        var currency = tokens.GetValueOrDefault("Payroll.Currency", "SAR");
                        string LabelOf(HR.Domain.Engines.Finance.Payslips.PayslipComponentLine l)
                            => payslipLabels is not null && payslipLabels.TryGetValue(l.ComponentCode, out var lbl) && !string.IsNullOrWhiteSpace(lbl)
                                ? lbl : l.ComponentCode;
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(2); });
                            void Section(string header, IReadOnlyList<HR.Domain.Engines.Finance.Payslips.PayslipComponentLine> lines, decimal total, string totalLabel)
                            {
                                table.Cell().ColumnSpan(2).Background(Colors.Grey.Lighten3).Padding(6).Text(header).Bold().FontSize(11);
                                foreach (var l in lines)
                                {
                                    table.Cell().Padding(6).Text(LabelOf(l)).FontSize(10);
                                    table.Cell().Padding(6).AlignLeft().Text($"{l.Amount:N2} {currency}");
                                }
                                table.Cell().Padding(6).Text(totalLabel).Bold().FontSize(10);
                                table.Cell().Padding(6).AlignLeft().Text($"{total:N2} {currency}").Bold();
                            }
                            Section(b.Label ?? "الإيرادات", payslip.Earnings, payslip.TotalEarnings, "إجمالي الإيرادات");
                            Section("الاستقطاعات", payslip.Deductions, payslip.TotalDeductions, "إجمالي الاستقطاعات");
                            table.Cell().Background(Colors.Grey.Lighten2).Padding(6).Text("صافي الراتب").Bold().FontSize(11);
                            table.Cell().Background(Colors.Grey.Lighten2).Padding(6).AlignLeft().Text($"{payslip.NetAmount:N2} {currency}").Bold().FontSize(11);
                        });
                    }
                    break;
                case "divider":
                    col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                    break;
                case "spacer":
                    col.Item().Height(b.Height ?? 16);
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(b.Text)) col.Item().Text(ResolveTokens(b.Text, tokens)).FontSize(11);
                    break;
            }
        }
    }

    private static IContainer Aligned(QuestPDF.Fluent.ColumnDescriptor col, string? align, string fallback)
    {
        var a = (align ?? fallback).ToLowerInvariant();
        var item = col.Item();
        return a switch { "center" => item.AlignCenter(), "left" => item.AlignLeft(), _ => item.AlignRight() };
    }

    private static void RenderLegacy(QuestPDF.Fluent.ColumnDescriptor col, List<(string kind, string text)> bb)
    {
        foreach (var (kind, text) in bb)
        {
            if (string.IsNullOrWhiteSpace(text) && kind != "hr") continue;
            switch (kind)
            {
                case "h1": col.Item().Text(text).FontSize(14).Bold(); break;
                case "h2": col.Item().Text(text).FontSize(12).Bold(); break;
                case "h3": col.Item().Text(text).FontSize(11).Bold(); break;
                case "li": col.Item().Text($"•  {text}").FontSize(11); break;
                case "hr": col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1); break;
                default: col.Item().Text(text).FontSize(11); break;
            }
        }
    }

    private static float SizePt(string? size, float def) => (size ?? "").ToLowerInvariant() switch
    {
        "sm" => 9, "md" => 11, "lg" => 15, "xl" => 20, _ => def,
    };

    private static string StripBraces(string? token) => (token ?? "").Trim().TrimStart('{').TrimEnd('}').Trim();

    // ── config helpers ──

    private static Dictionary<string, JsonElement>? ParseCfg(string? json) => ParseJson(json);
    private static bool GetBool(Dictionary<string, JsonElement>? d, string key, bool def)
    {
        if (d is not null && d.TryGetValue(key, out var v))
        {
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
        }
        return def;
    }
    private static string? GetStr(Dictionary<string, JsonElement>? d, string key)
        => d is not null && d.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static float GetNum(Dictionary<string, JsonElement>? d, string key, float def)
        => d is not null && d.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : def;

    // ── shared helpers ──

    private static byte[] MakeQr(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(20);
    }

    private async Task<byte[]?> LoadImageAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var idx = url.LastIndexOf('/');
        var idPart = idx >= 0 ? url[(idx + 1)..] : url;
        if (!Guid.TryParse(idPart, out var fileId)) return null;
        return await _db.Files.Where(f => f.Id == fileId).Select(f => f.Data).FirstOrDefaultAsync(ct);
    }

    private static Dictionary<string, JsonElement>? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json); } catch { return null; }
    }

    private static string? Str(Dictionary<string, JsonElement>? d, string key)
        => d is not null && d.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string StatusAr(string status) => status switch
    {
        "Approved" => "تمت الموافقة", "Rejected" => "مرفوض", "Pending" => "بانتظار", "Skipped" => "تم التخطي", _ => status,
    };

    /// <summary>Replace {{Token.Path}} placeholders with resolved values.</summary>
    public static string ResolveTokens(string template, IReadOnlyDictionary<string, string> tokens)
        => System.Text.RegularExpressions.Regex.Replace(template, @"\{\{\s*([\w.]+)\s*\}\}",
            m => tokens.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    /// <summary>Parse a token-resolved HTML body into a renderable block sequence (legacy subset).</summary>
    public static List<(string kind, string text)> ParseHtmlBlocks(string html)
    {
        var s = html ?? "";
        var R = System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        string Rep(string input, string pat, string repl) => System.Text.RegularExpressions.Regex.Replace(input, pat, repl, R);

        s = Rep(s, @"<br\s*/?>", "\n");
        s = Rep(s, @"<hr\s*/?>", "\n@@HR@@\n");
        s = Rep(s, @"<h1[^>]*>(.*?)</h1>", "\n@@H1@@$1\n");
        s = Rep(s, @"<h2[^>]*>(.*?)</h2>", "\n@@H2@@$1\n");
        s = Rep(s, @"<h3[^>]*>(.*?)</h3>", "\n@@H3@@$1\n");
        s = Rep(s, @"<li[^>]*>(.*?)</li>", "\n@@LI@@$1\n");
        s = Rep(s, @"</(p|div|tr)>", "\n");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"<[^>]+>", "", R);
        s = s.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"");

        var blocks = new List<(string, string)>();
        foreach (var raw in s.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("@@HR@@")) { blocks.Add(("hr", "")); continue; }
            if (line.StartsWith("@@H1@@")) { blocks.Add(("h1", line[6..].Trim())); continue; }
            if (line.StartsWith("@@H2@@")) { blocks.Add(("h2", line[6..].Trim())); continue; }
            if (line.StartsWith("@@H3@@")) { blocks.Add(("h3", line[6..].Trim())); continue; }
            if (line.StartsWith("@@LI@@")) { blocks.Add(("li", line[6..].Trim())); continue; }
            blocks.Add(("p", line));
        }
        return blocks;
    }

    // ── block model DTOs (deserialized from DocumentTemplate.LayoutJson) ──
    private sealed class Layout { public List<Block>? Blocks { get; set; } }
    private sealed class Block
    {
        public string? Type { get; set; }
        public string? Text { get; set; }
        public string? Token { get; set; }
        public string? Align { get; set; }
        public string? Size { get; set; }
        public bool Bold { get; set; }
        public string? FileId { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string? Role { get; set; }
        public string? Label { get; set; }
        public List<Row>? Rows { get; set; }
    }
    private sealed class Row { public string? Label { get; set; } public string? Value { get; set; } }
}
