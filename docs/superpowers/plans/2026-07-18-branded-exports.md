# Branded, Arabic-correct Exports — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]`.

**Goal:** Fix `???` Arabic in PDF exports and add a company header (logo + name + CR/VAT/contact) to PDF and Excel exports (widgets AND reports). No engine change to the query side; no migration.

**Architecture:** `PdfExportWriter` registers the existing Tajawal Arabic font + RTL (same as `DocumentRenderer`). A `CompanyBranding` record is added to `ExportWriteOptions`; the export services load the `CompanyProfile` + logo bytes and pass branding into `writer.Write(dataset, options)`; the PDF and Excel writers render a header block when branding is present. CSV/Txt/Xml and Payroll's own exports are unaffected (branding is opt-in).

**Tech Stack:** .NET 8, QuestPDF (PDF), ClosedXML (Excel), xUnit + FluentAssertions.

## Global Constraints
- Reuse the existing Tajawal TTFs already copied to `AppContext.BaseDirectory/Fonts/*.ttf` (via `HR.Api.csproj`). Do NOT add font assets.
- Branding is opt-in via `ExportWriteOptions.Branding`; writers render the header ONLY when it's non-null — so Payroll's existing exports (which pass no branding) are byte-unchanged except the PDF Arabic-font fix (a pure improvement).
- Logo loading mirrors `DocumentRenderer.LoadImageAsync`: `LogoUrl` is `"/api/files/{guid}"`; bytes are `_db.Files.Where(f => f.Id == guid).Select(f => f.Data)`.
- All logo rendering wrapped in try/catch — a bad/absent logo must never break the export (degrade to text-only header).
- `TabularDataset` columns expose `.Header` (display) and `.Key` (row lookup). `ExportValue.Format(v)` formats a cell.

## Confirmed facts
- Font: family `"Tajawal"`; registration pattern (from `DocumentRenderer` static ctor):
  ```csharp
  var dir = Path.Combine(AppContext.BaseDirectory, "Fonts");
  if (Directory.Exists(dir))
      foreach (var f in Directory.GetFiles(dir, "*.ttf"))
          QuestPDF.Drawing.FontManager.RegisterFont(File.OpenRead(f));
  ```
  RTL+font per page: `page.DefaultTextStyle(x => x.FontFamily("Tajawal").FontSize(9).DirectionFromRightToLeft());`
- `ExportWriteOptions` today: `public sealed record ExportWriteOptions(char Delimiter = '\t', bool FixedWidth = false, bool IncludeHeader = true);` in `backend/src/HR.Application/Engines/Finance/Export/TabularDataset.cs`.
- `IExportWriter.Write(TabularDataset data, ExportWriteOptions? options = null) : byte[]`.
- `CompanyProfile` (`HR.Domain.Engines.CompanyConfig`): `NameEn, NameAr, LogoUrl (string?), CommercialRegistration, VatNumber, Phone, Email, Address, City, Country`. `DbSet` = `_db.CompanyProfiles`; files = `_db.Files` (`StoredFile.Data : byte[]`).
- `WidgetExportService` ctor: `(IWidgetDataService data, IEnumerable<IExportWriter> writers, ApplicationDbContext db)` — call sites `ExportAsync` + `ExportRowsAsync` currently `writer.Write(dataset)` (no options). `ReportExportService.ExportAsync` similarly `writer.Write(dataset)` — CONFIRM it has `ApplicationDbContext` injected (it queries reports); if not, add it.
- PdfExportWriter current: `page.DefaultTextStyle(x => x.FontSize(9)); page.Header().Text(data.Title)...` then a `Table`. ExcelExportWriter current: writes column headers at row 1, data from row 2; `ws.RightToLeft = true`.

---

## Task 1: CompanyBranding + ExportWriteOptions + BrandingLoader (TDD helper)

**Files:**
- Modify: `backend/src/HR.Application/Engines/Finance/Export/TabularDataset.cs`
- Create: `backend/src/HR.Modules/Platform/Services/Reports/ExportBrandingLoader.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/ExportBrandingLoaderTests.cs`

- [ ] **Step 1: Add `CompanyBranding` + extend `ExportWriteOptions`** in `TabularDataset.cs`:
```csharp
public sealed record CompanyBranding(
    string? NameAr, string? NameEn, byte[]? LogoBytes,
    string? CommercialRegistration, string? VatNumber,
    string? Phone, string? Email, string? Address);

public sealed record ExportWriteOptions(
    char Delimiter = '\t', bool FixedWidth = false, bool IncludeHeader = true,
    CompanyBranding? Branding = null);
```

- [ ] **Step 2: Write the failing test** for the pure Guid extraction:
```csharp
using FluentAssertions;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ExportBrandingLoaderTests
{
    [Theory]
    [InlineData("/api/files/8d1e9b7a-1111-2222-3333-444455556666", true)]
    [InlineData("8d1e9b7a-1111-2222-3333-444455556666", true)]
    [InlineData("/api/files/not-a-guid", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void TryGetFileId_parses_guid_from_url_tail(string? url, bool expected)
        => ExportBrandingLoader.TryGetFileId(url, out _).Should().Be(expected);
}
```

- [ ] **Step 3: Run to verify FAIL** — `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~ExportBrandingLoaderTests` → FAIL (loader missing).

- [ ] **Step 4: Implement `ExportBrandingLoader.cs`:**
```csharp
using HR.Application.Engines.Finance.Export;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Loads the tenant's CompanyProfile + logo bytes into a CompanyBranding for export headers.</summary>
public static class ExportBrandingLoader
{
    public static bool TryGetFileId(string? url, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(url)) return false;
        var idx = url.LastIndexOf('/');
        var tail = idx >= 0 ? url[(idx + 1)..] : url;
        return Guid.TryParse(tail, out id);
    }

    public static async Task<CompanyBranding?> LoadAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var c = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        if (c is null) return null;
        byte[]? logo = null;
        if (TryGetFileId(c.LogoUrl, out var fileId))
            logo = await db.Files.Where(f => f.Id == fileId).Select(f => f.Data).FirstOrDefaultAsync(ct);
        return new CompanyBranding(c.NameAr, c.NameEn, logo, c.CommercialRegistration, c.VatNumber, c.Phone, c.Email, c.Address);
    }
}
```
> Confirm `CompanyProfile` property names against the entity; adjust if any differ.

- [ ] **Step 5: Run to verify PASS.** Build `HR.Api` too (`dotnet build backend/src/HR.Api/HR.Api.csproj -v q`).
- [ ] **Step 6: Commit** `git add backend/src/HR.Application/Engines/Finance/Export/TabularDataset.cs backend/src/HR.Modules/Platform/Services/Reports/ExportBrandingLoader.cs backend/tests/HR.Modules.Platform.Tests/Reports/ExportBrandingLoaderTests.cs && git commit -m "feat(export): CompanyBranding option + ExportBrandingLoader"`

---

## Task 2: PdfExportWriter — Arabic font + RTL + company header

**Files:** Modify `backend/src/HR.Modules/Platform/Services/Reports/PdfExportWriter.cs`; Test `backend/tests/HR.Modules.Platform.Tests/Reports/PdfExportWriterBrandingTests.cs`.

- [ ] **Step 1: Register the font in the static ctor** (add after the license line):
```csharp
static PdfExportWriter()
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
```

- [ ] **Step 2: Apply font+RTL and render the branding header.** In `Write`, change the default style and header:
```csharp
page.DefaultTextStyle(x => x.FontFamily("Tajawal").FontSize(9).DirectionFromRightToLeft());
page.Header().Element(h => ComposeHeader(h, data.Title, options?.Branding));
```
Add the header composer to the class:
```csharp
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
                    catch { /* bad logo -> skip */ }
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
```
> Adjust QuestPDF fluent calls to the installed version's API if the compiler flags any method (`FitArea`, `LineHorizontal`, `ConstantItem` are all standard). Keep the table body unchanged.

- [ ] **Step 3: Write a smoke test** (PDF bytes are opaque; assert it produces a non-empty PDF with Arabic title + branding without throwing):
```csharp
using System.Collections.Generic;
using FluentAssertions;
using HR.Application.Engines.Finance.Export;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class PdfExportWriterBrandingTests
{
    private static TabularDataset Data() => new(
        "موظفون حسب الإدارات",
        new List<TabularColumn> { new("name", "اسم الموظف"), new("dept", "الإدارة") },
        new List<IReadOnlyDictionary<string, object?>> { new Dictionary<string, object?> { ["name"] = "رُبا", ["dept"] = "الموارد البشرية" } });

    [Fact]
    public void Write_with_arabic_and_branding_produces_pdf()
    {
        var branding = new CompanyBranding("شركة الاختبار", "Test Co", null, "1010101010", "300000000000003", "0500000000", "hr@test.sa", "الرياض");
        var bytes = new PdfExportWriter().Write(Data(), new ExportWriteOptions(Branding: branding));
        bytes.Should().NotBeNullOrEmpty();
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-"); // valid PDF magic
    }

    [Fact]
    public void Write_without_branding_still_works()
        => new PdfExportWriter().Write(Data(), null).Should().NotBeNullOrEmpty();
}
```
> Adapt the `TabularDataset`/`TabularColumn` ctor to the REAL shapes (read `TabularDataset.cs` — the column may be `new TabularColumn(Key, Header)` or a record with named props; the row list element type must match).

- [ ] **Step 4: Run to verify GREEN** (`--filter FullyQualifiedName~PdfExportWriterBrandingTests`). Both tests pass.
- [ ] **Step 5: Commit** `git add backend/src/HR.Modules/Platform/Services/Reports/PdfExportWriter.cs backend/tests/HR.Modules.Platform.Tests/Reports/PdfExportWriterBrandingTests.cs && git commit -m "fix(export): Arabic font+RTL + company header in PDF export"`

---

## Task 3: ExcelExportWriter — company header + logo

**Files:** Modify `backend/src/HR.Modules/Payroll/Export/ExcelExportWriter.cs`; Test `backend/tests/HR.Modules.Platform.Tests/...` — NOTE the Excel writer is in the Payroll module; put the test in the Payroll test project (`backend/tests/HR.Modules.Payroll.Tests/` if it exists) OR a build-only verification if no suitable test project references ClosedXML. Prefer a smoke test in whichever test project already references the Payroll module + ClosedXML; if none, skip the unit test and rely on build + live-verify (note this in the report).

- [ ] **Step 1: Render a branding header block before the table.** Rewrite `Write` so that when `options?.Branding` is non-null it writes a header block occupying the top rows, then the column headers + data start below it:
```csharp
public byte[] Write(TabularDataset data, ExportWriteOptions? options = null)
{
    using var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add(SheetName(data.Title));
    ws.RightToLeft = true;

    int headerOffset = 0;
    var b = options?.Branding;
    if (b is not null)
    {
        var name = string.IsNullOrWhiteSpace(b.NameAr) ? b.NameEn : b.NameAr;
        if (!string.IsNullOrWhiteSpace(name)) { var cell = ws.Cell(1, 1); cell.Value = name; cell.Style.Font.Bold = true; cell.Style.Font.FontSize = 14; }
        var meta = string.Join("   |   ", new[]
        {
            string.IsNullOrWhiteSpace(b.CommercialRegistration) ? null : $"س.ت: {b.CommercialRegistration}",
            string.IsNullOrWhiteSpace(b.VatNumber) ? null : $"الرقم الضريبي: {b.VatNumber}",
            string.IsNullOrWhiteSpace(b.Phone) ? null : b.Phone,
            string.IsNullOrWhiteSpace(b.Email) ? null : b.Email,
            string.IsNullOrWhiteSpace(b.Address) ? null : b.Address,
        }.Where(s => s is not null));
        if (meta.Length > 0) ws.Cell(2, 1).Value = meta;
        headerOffset = 3; // rows 1-2 text + row 3 spacer; column headers start at row 4
        if (b.LogoBytes is { Length: > 0 })
        {
            try { using var img = new MemoryStream(b.LogoBytes); ws.AddPicture(img).MoveTo(ws.Cell(1, Math.Max(1, data.Columns.Count))).WithSize(120, 48); }
            catch { /* bad logo -> skip */ }
        }
    }

    int headerRow = headerOffset + 1;
    for (int c = 0; c < data.Columns.Count; c++)
    {
        var cell = ws.Cell(headerRow, c + 1);
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
            var cell = ws.Cell(headerRow + 1 + i, c + 1);
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
```
> Keep `SheetName` unchanged. Adjust `WithSize`/`MoveTo`/`AddPicture` to the installed ClosedXML API if flagged (all standard). The logo `MoveTo` target uses the last column so it doesn't overlap the name text.

- [ ] **Step 2:** Build the Payroll module: `dotnet build backend/src/HR.Modules/Payroll/HR.Modules.Payroll.csproj -v q` → 0 errors. If a Payroll test project exists, add a smoke test asserting `Write(..., new ExportWriteOptions(Branding: b))` returns non-empty bytes starting with the ZIP magic `PK`; else note build-only verification.
- [ ] **Step 3: Commit** `git add backend/src/HR.Modules/Payroll/Export/ExcelExportWriter.cs <test if any> && git commit -m "feat(export): company header + logo in Excel export"`

---

## Task 4: Wire branding into the export services

**Files:** Modify `backend/src/HR.Modules/Platform/Services/WidgetData/WidgetExportService.cs` (ExportAsync + ExportRowsAsync), `backend/src/HR.Modules/Platform/Services/Reports/ReportExportService.cs` (ExportAsync).

- [ ] **Step 1:** In each of the three export methods, load branding once and pass it. Before the `writer.Write(dataset...)` call, add:
```csharp
var branding = await ExportBrandingLoader.LoadAsync(_db, ct);
var bytes = writer.Write(dataset, new ExportWriteOptions(Branding: branding));
```
Replace the existing `writer.Write(dataset)` with the branded call. Ensure `_db` (`ApplicationDbContext`) is available — `WidgetExportService` already injects it; `ReportExportService` — confirm it injects `ApplicationDbContext`; if not, add it to the ctor (it likely already has it for report queries). Add `using HR.Modules.Platform.Services.Reports;` / `using HR.Application.Engines.Finance.Export;` as needed.
- [ ] **Step 2:** Build `HR.Api`: `dotnet build backend/src/HR.Api/HR.Api.csproj -v q` → 0 errors.
- [ ] **Step 3: Commit** `git add backend/src/HR.Modules/Platform/Services/WidgetData/WidgetExportService.cs backend/src/HR.Modules/Platform/Services/Reports/ReportExportService.cs && git commit -m "feat(export): load company branding + pass to writers (widget + report export)"`

---

## Task 5: Full build + test gate
- [ ] `dotnet build backend/HR.sln -v q` → 0 errors; `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --nologo` → all pass. Commit if incidental.

---

## Self-Review
- Arabic `???` fixed → Task 2 (font register + FontFamily + RTL). ✅ (fixes widget + report PDF exports since both use `PdfExportWriter`.)
- Company name/logo/data on PDF → Task 2 header. ✅
- Company name/logo/data on Excel → Task 3 header + logo. ✅
- Branding loaded from CompanyProfile + logo bytes → Tasks 1,4. ✅
- Opt-in (Payroll/CSV unaffected) → branding only rendered when non-null; only widget/report services pass it. ✅
- No migration, no query-engine change → all tasks. ✅
- Testing: pure `TryGetFileId` (T1), PDF smoke (T2), Excel smoke/build (T3); live-verify the actual PDF post-deploy. ✅

**Type consistency:** `CompanyBranding(NameAr, NameEn, LogoBytes, CommercialRegistration, VatNumber, Phone, Email, Address)` identical in T1 (record), T2 (PDF header), T3 (Excel header), T4 (loader output). `ExportWriteOptions(..., Branding)` used consistently. `ExportBrandingLoader.LoadAsync(_db, ct)` / `TryGetFileId(url, out id)` consistent T1↔T4.
