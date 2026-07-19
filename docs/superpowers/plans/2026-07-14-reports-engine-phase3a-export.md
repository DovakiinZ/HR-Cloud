# Reports Engine Phase 3a — Export (Excel/CSV/PDF) + Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Add `GET /api/platform/reports/{id}/export?format=excel|csv|pdf` that runs a report to completion and streams a downloadable file via the existing `IExportWriter` framework (plus a new QuestPDF `PdfExportWriter`), and clear the two non-blocking Phase-2 hardening findings.

**Architecture:** A pure `ReportResultFlattener` turns a `ReportResult` (columns + nested groups + aggregates) into the framework's `TabularDataset`. A new `RunForExportAsync` runs the report without the 200-row page clamp (up to `RowCap`). `ReportExportService` (access-gated) ties run→flatten→writer together and resolves the writer by `ExportFormat`. Excel/CSV writers already exist; a new `PdfExportWriter : IExportWriter` reuses the already-referenced QuestPDF. Export is gated by the already-seeded `Platform.Reports.Export` permission.

**Tech Stack:** .NET 8, EF Core 8, MediatR, QuestPDF 2026.6.0 (already referenced in `HR.Modules.Platform`), ClosedXML (via existing `ExcelExportWriter`), xUnit + FluentAssertions.

## Global Constraints

- **Reuse the export framework — do NOT fork it.** `IExportWriter` / `ExportFormat` / `TabularDataset` / `TabularColumn` / `ExportWriteOptions` live in `HR.Application.Engines.Finance.Export` (accessible from Platform via its existing `HR.Application` project ref). Writers are resolved at runtime via `IEnumerable<IExportWriter>` (`w => w.Format == format`). Do NOT add a project reference to `HR.Modules.Payroll`.
- **`TabularDataset` shape (exact):** `TabularDataset(string Title, IReadOnlyList<TabularColumn> Columns, IReadOnlyList<IReadOnlyDictionary<string,object?>> Rows)`. `TabularColumn(string Key, string Header, TabularAlign Align = Start, int? Width = null)`. `TabularAlign { Start, End, Center }`. Row dicts are keyed by `TabularColumn.Key`.
- **Access:** export is gated by `IReportAccessService.EnsureCanReadAsync` (throws `ForbiddenException`/`NotFoundException`) AND `[RequirePermission("Platform.Reports.Export")]` (already seeded — do NOT add new permission strings).
- **PDF:** reuse QuestPDF (already in `HR.Modules.Platform.csproj`, `LicenseType.Community` is set globally in `DocumentRenderer`'s static ctor — do NOT set the license again). The Tajawal font is loaded from `AppDomain.CurrentDomain.BaseDirectory/Fonts/` by `DocumentRenderer`; the report PDF may use the default font (no RTL-specific requirement for R1 export beyond legibility).
- **MediatR shapes (verified in Phase 2):** no-response = `record : IRequest;` + `IRequestHandler<T>` `Task Handle`. `ForbiddenException`/`NotFoundException`/`ValidationException` are in `HR.Application.Common.Exceptions`. `ValidationException` has NO string ctor — use `new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("Field", "message") })` (as done in `ReportShareCommands`).
- **Namespaces:** export service/writer/flattener in `HR.Modules.Platform.Services.Reports`; commands/queries under the existing `HR.Modules.Platform.{Commands,Queries}.Reports`.
- DB-touching tests are `[SkippableFact]` gated on `REPORTS_TEST_DB`. Pure logic gets DB-free xUnit tests. Commit after each task (`feat(reports):` / `fix(reports):` / `test(reports):`).

---

## File Structure

- `backend/src/HR.Modules/Platform/Commands/Reports/ReportTagCommands.cs` *(modify — hardening H1)*
- `backend/src/HR.Application/Engines/Finance/Export/TabularDataset.cs` *(modify — add `Pdf` to `ExportFormat`)*
- `backend/src/HR.Modules/Platform/Services/Reports/PdfExportWriter.cs` *(new — QuestPDF `IExportWriter`)*
- `backend/src/HR.Modules/Platform/Services/Reports/IReportExecutionService.cs` + `ReportExecutionService.cs` *(modify — `RunForExportAsync`)*
- `backend/src/HR.Modules/Platform/Services/Reports/ReportResultFlattener.cs` *(new — pure `ReportResult → TabularDataset`)*
- `backend/src/HR.Modules/Platform/Services/Reports/IReportExportService.cs` + `ReportExportService.cs` *(new)*
- `backend/src/HR.Modules/Platform/Queries/Reports/ReportExportQueries.cs` *(new — `ExportReportQuery`)*
- `backend/src/HR.Modules/Platform/Controllers/ReportsController.cs` *(modify — export endpoint)*
- `backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs` *(modify — register `PdfExportWriter`, `IReportExportService`)*
- Tests under `backend/tests/HR.Modules.Platform.Tests/Reports/`.

---

## Task H1: Hardening — tag dup-name guard + tag-assign existence check

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Commands/Reports/ReportTagCommands.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/ReportTagHardeningTests.cs`

**Interfaces:**
- Consumes: `_db.ReportTags`, `_db.ReportDefinitionTags`, `ValidationException`, `NotFoundException`.
- Produces: no new types — behavior change: `CreateReportTagCommand` rejects a duplicate `(tenant) Name` with a 400; `AssignReportTagCommand` throws `NotFoundException` when the tag id doesn't exist.

- [ ] **Step 1: Write the failing test** (`[SkippableFact]`, mirror the `StubUser`+`Conn` harness from `ReportShareCommandTests.cs`)

```csharp
[SkippableFact]
public async Task Create_tag_rejects_duplicate_name()
{
    Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
    var tenant = Guid.NewGuid(); var user = new StubUser(Guid.NewGuid(), tenant);
    var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Conn).Options;
    await using var db = new ApplicationDbContext(opts, user);
    await using var tx = await db.Database.BeginTransactionAsync();
    var mapper = new AutoMapper.MapperConfiguration(c => c.AddProfile<HR.Modules.Platform.MappingProfiles.PlatformMappingProfile>()).CreateMapper();
    var name = "Q" + Guid.NewGuid().ToString("N")[..6];
    await new CreateReportTagCommandHandler(db, mapper).Handle(new CreateReportTagCommand(name, null), default);
    var act = async () => await new CreateReportTagCommandHandler(db, mapper).Handle(new CreateReportTagCommand(name, null), default);
    await act.Should().ThrowAsync<HR.Application.Common.Exceptions.ValidationException>();
    await tx.RollbackAsync();
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~ReportTagHardeningTests` → locally Skipped; the point is it compiles and the guard doesn't yet exist. (RED = the guard is missing; assert via build + the test being present.)

- [ ] **Step 3: Add the guards** in `ReportTagCommands.cs`:

In `CreateReportTagCommandHandler.Handle`, before `_db.ReportTags.Add(e)`:
```csharp
var dup = await _db.ReportTags.AnyAsync(t => t.Name == r.Name, ct);
if (dup) throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("Name", $"A tag named '{r.Name}' already exists.") });
```
Add `using Microsoft.EntityFrameworkCore;`, `using HR.Application.Common.Exceptions;` if missing.

In `AssignReportTagCommandHandler.Handle`, after `EnsureCanEditAsync` and before inserting the link:
```csharp
var tagExists = await _db.ReportTags.AnyAsync(t => t.Id == r.ReportTagId, ct);
if (!tagExists) throw new NotFoundException("ReportTag", r.ReportTagId);
```

- [ ] **Step 4: Build + test** — `dotnet build backend/src/HR.Api/HR.Api.csproj` (0 errors); `dotnet test backend/tests/HR.Modules.Platform.Tests` (green; new test skipped locally).

- [ ] **Step 5: Commit**
```bash
git add backend/src/HR.Modules/Platform/Commands/Reports/ReportTagCommands.cs backend/tests/HR.Modules.Platform.Tests/Reports/ReportTagHardeningTests.cs
git commit -m "fix(reports): tag dup-name guard (400) + tag-assign existence check (404)"
```

---

## Task E1: `RunForExportAsync` — full-result execution (no 200-row page clamp)

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Services/Reports/IReportExecutionService.cs`
- Modify: `backend/src/HR.Modules/Platform/Services/Reports/ReportExecutionService.cs`

**Interfaces:**
- Produces: `Task<ReportResult> RunForExportAsync(Guid reportId, CancellationToken ct)` — runs the same pipeline as `RunAsync` but returns ALL rows up to `RowCap` (no per-page slice), with `Truncated=true` when the row count exceeds `RowCap`. Consumed by `ReportExportService` (Task E4).

- [ ] **Step 1: Read the current `RunAsync`** in `ReportExecutionService.cs` to see the pipeline (resolve → SQL → materialize → shape → page). The change: extract the shared body into a private `RunCoreAsync(Guid reportId, int page, int pageSize, CancellationToken ct)` (the current `RunAsync` body verbatim), then:
  - `RunAsync` calls `RunCoreAsync(reportId, Math.Max(1,page), Math.Clamp(pageSize,1,200), ct)`.
  - `RunForExportAsync` calls `RunCoreAsync(reportId, 1, RowCap, ct)` (page 1, page size = `RowCap`, so the shaper returns the full set; `RowCap+1` fetch still sets `Truncated`).

  If the current paging is applied by the shaper via page/pageSize, passing `pageSize=RowCap` returns everything. If paging is applied differently, adapt so `RunForExportAsync` returns the full shaped result. Confirm by reading the shaper call.

- [ ] **Step 2: Add the interface method** to `IReportExecutionService.cs`:
```csharp
Task<ReportResult> RunForExportAsync(Guid reportId, CancellationToken ct);
```

- [ ] **Step 3: Implement** in `ReportExecutionService.cs` per Step 1 (extract `RunCoreAsync`, add `RunForExportAsync`). Do NOT change `RunAsync`'s external behavior.

- [ ] **Step 4: Build** — `dotnet build backend/src/HR.Api/HR.Api.csproj` (0 errors). (No new unit test required; the existing integration test still exercises `RunAsync`; `RunForExportAsync` is exercised end-to-end by E4's `[SkippableFact]`.)

- [ ] **Step 5: Commit**
```bash
git add backend/src/HR.Modules/Platform/Services/Reports/IReportExecutionService.cs backend/src/HR.Modules/Platform/Services/Reports/ReportExecutionService.cs
git commit -m "feat(reports): RunForExportAsync returns full result up to RowCap (no page clamp)"
```

---

## Task E2: `ReportResultFlattener` — pure `ReportResult → TabularDataset`

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/Reports/ReportResultFlattener.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/ReportResultFlattenerTests.cs`

**Interfaces:**
- Consumes: `ReportResult`, `ReportColumn`, `ReportGroup`, `ReportRow` (from `ReportModels.cs`); `TabularDataset`, `TabularColumn`, `TabularAlign` (from `HR.Application.Engines.Finance.Export`).
- Produces: `public static TabularDataset ReportResultFlattener.Flatten(ReportResult result, string title)`.

Flattening rules:
- **Columns:** one `TabularColumn` per `result.Columns`, `Key = c.Code`, `Header = c.Label`, `Align = c.IsMeasure ? TabularAlign.End : TabularAlign.Start`.
- **Rows (flat report, `result.Groups` empty):** each `result.Rows` → a dict keyed by column code (`col.Code → row[col.Code]` or null if absent).
- **Rows (grouped):** depth-first over `result.Groups`; for each group, first recurse its `SubGroups` (if any) else emit its `Rows` as data rows, then emit ONE subtotal row: the group's `FieldCode` column cell = `$"{group.Label} — subtotal"`, each measure column cell = `group.Aggregates.GetValueOrDefault(col.Code)` (only for columns where `col.IsMeasure` and the aggregate key exists), other cells null.
- **Grand total:** if `result.GrandTotals` non-empty, append ONE row: the FIRST column's cell = `"Grand Total"`, each measure column cell = `result.GrandTotals.GetValueOrDefault(col.Code)`.

- [ ] **Step 1: Write the failing tests** (pure, no DB):

```csharp
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using HR.Application.Engines.Finance.Export;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ReportResultFlattenerTests
{
    private static ReportColumn Dim(string code) => new() { Code = code, Label = code, IsMeasure = false };
    private static ReportColumn Measure(string code) => new() { Code = code, Label = code, IsMeasure = true };

    [Fact]
    public void Flat_report_projects_rows_and_columns()
    {
        var result = new ReportResult
        {
            Columns = new() { Dim("Name"), Measure("Salary") },
            Rows = new()
            {
                new ReportRow(new Dictionary<string, object?> { ["Name"] = "A", ["Salary"] = 100.0 }),
                new ReportRow(new Dictionary<string, object?> { ["Name"] = "B", ["Salary"] = 200.0 }),
            },
        };
        var ds = ReportResultFlattener.Flatten(result, "T");
        ds.Columns.Select(c => c.Key).Should().Equal("Name", "Salary");
        ds.Columns.Single(c => c.Key == "Salary").Align.Should().Be(TabularAlign.End);
        ds.Rows.Should().HaveCount(2);
        ds.Rows[0]["Name"].Should().Be("A");
        ds.Rows[1]["Salary"].Should().Be(200.0);
    }

    [Fact]
    public void Grouped_report_emits_data_rows_then_subtotal_then_grand_total()
    {
        var result = new ReportResult
        {
            Columns = new() { Dim("Dept"), Measure("Salary") },
            Groups = new()
            {
                new ReportGroup
                {
                    FieldCode = "Dept", Key = "HR", Label = "HR",
                    Rows = new() { new ReportRow(new Dictionary<string, object?> { ["Dept"] = "HR", ["Salary"] = 100.0 }) },
                    Aggregates = new() { ["Salary"] = 100.0 },
                },
            },
            GrandTotals = new() { ["Salary"] = 100.0 },
        };
        var ds = ReportResultFlattener.Flatten(result, "T");
        // 1 data row + 1 subtotal + 1 grand total
        ds.Rows.Should().HaveCount(3);
        ds.Rows[1]["Dept"].Should().Be("HR — subtotal");
        ds.Rows[1]["Salary"].Should().Be(100.0);
        ds.Rows[2]["Dept"].Should().Be("Grand Total");
        ds.Rows[2]["Salary"].Should().Be(100.0);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~ReportResultFlattenerTests` → FAIL (type missing).

- [ ] **Step 3: Implement** `ReportResultFlattener.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using HR.Application.Engines.Finance.Export;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Pure projection of an executed ReportResult into the tabular export payload.
/// Grouped reports flatten depth-first: data rows, then a per-group subtotal row, then a grand-total row.</summary>
public static class ReportResultFlattener
{
    public static TabularDataset Flatten(ReportResult result, string title)
    {
        var columns = result.Columns
            .Select(c => new TabularColumn(c.Code, c.Label, c.IsMeasure ? TabularAlign.End : TabularAlign.Start))
            .ToList();

        var rows = new List<IReadOnlyDictionary<string, object?>>();

        if (result.Groups.Count > 0)
            foreach (var g in result.Groups) EmitGroup(g, result.Columns, rows);
        else
            foreach (var r in result.Rows) rows.Add(ProjectRow(r, result.Columns));

        if (result.GrandTotals.Count > 0)
            rows.Add(TotalRow("Grand Total", result.Columns, result.GrandTotals, result.Columns.FirstOrDefault()?.Code));

        return new TabularDataset(title, columns, rows);
    }

    private static void EmitGroup(ReportGroup g, List<ReportColumn> cols, List<IReadOnlyDictionary<string, object?>> rows)
    {
        if (g.SubGroups.Count > 0)
            foreach (var sub in g.SubGroups) EmitGroup(sub, cols, rows);
        else
            foreach (var r in g.Rows) rows.Add(ProjectRow(r, cols));

        // subtotal row keyed on the group's dimension column
        rows.Add(TotalRow($"{g.Label} — subtotal", cols, g.Aggregates, g.FieldCode));
    }

    private static IReadOnlyDictionary<string, object?> ProjectRow(ReportRow row, List<ReportColumn> cols)
    {
        var d = new Dictionary<string, object?>();
        foreach (var c in cols) d[c.Code] = row.TryGetValue(c.Code, out var v) ? v : null;
        return d;
    }

    private static IReadOnlyDictionary<string, object?> TotalRow(string label, List<ReportColumn> cols, IReadOnlyDictionary<string, double> aggregates, string? labelColumn)
    {
        var d = new Dictionary<string, object?>();
        foreach (var c in cols) d[c.Code] = null;
        if (labelColumn is not null && d.ContainsKey(labelColumn)) d[labelColumn] = label;
        foreach (var c in cols.Where(c => c.IsMeasure))
            if (aggregates.TryGetValue(c.Code, out var v)) d[c.Code] = v;
        return d;
    }
}
```

- [ ] **Step 4: Run to verify it passes** — same filter → PASS (2 tests).

- [ ] **Step 5: Commit**
```bash
git add backend/src/HR.Modules/Platform/Services/Reports/ReportResultFlattener.cs backend/tests/HR.Modules.Platform.Tests/Reports/ReportResultFlattenerTests.cs
git commit -m "feat(reports): pure ReportResult -> TabularDataset flattener (rows, subtotals, grand total)"
```

---

## Task E3: `ExportFormat.Pdf` + `PdfExportWriter` (QuestPDF) + DI

**Files:**
- Modify: `backend/src/HR.Application/Engines/Finance/Export/TabularDataset.cs` (enum)
- Create: `backend/src/HR.Modules/Platform/Services/Reports/PdfExportWriter.cs`
- Modify: `backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/PdfExportWriterTests.cs`

**Interfaces:**
- Produces: `ExportFormat.Pdf = 5`; `PdfExportWriter : IExportWriter` (`Format = ExportFormat.Pdf`, `ContentType = "application/pdf"`, `Extension = "pdf"`, `byte[] Write(TabularDataset, ExportWriteOptions?)`).

- [ ] **Step 1: Add the enum value** in `TabularDataset.cs`:
```csharp
public enum ExportFormat { Excel = 1, Csv = 2, Txt = 3, Xml = 4, Pdf = 5 }
```

- [ ] **Step 2: Write the failing test** (pure — no DB; QuestPDF renders in-memory):
```csharp
using System.Collections.Generic;
using FluentAssertions;
using HR.Application.Engines.Finance.Export;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class PdfExportWriterTests
{
    [Fact]
    public void Writes_a_nonempty_pdf_document()
    {
        var ds = new TabularDataset("Employees", new List<TabularColumn>
            { new("Name", "Name"), new("Salary", "Salary", TabularAlign.End) },
            new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["Name"] = "Alice", ["Salary"] = 5000m },
                new Dictionary<string, object?> { ["Name"] = "Bob",   ["Salary"] = 7000m },
            });
        var writer = new PdfExportWriter();
        writer.Format.Should().Be(ExportFormat.Pdf);
        writer.ContentType.Should().Be("application/pdf");
        var bytes = writer.Write(ds);
        bytes.Should().NotBeNullOrEmpty();
        // PDF magic header "%PDF"
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }
}
```

- [ ] **Step 3: Run to verify it fails** — filter `~PdfExportWriterTests` → FAIL (type missing).

- [ ] **Step 4: Implement** `PdfExportWriter.cs` using QuestPDF Fluent (the license is already set globally by `DocumentRenderer`'s static ctor; do NOT set it again):

```csharp
using System.Linq;
using HR.Application.Engines.Finance.Export;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HR.Modules.Platform.Services.Reports;

public sealed class PdfExportWriter : IExportWriter
{
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
```
Note: `ExportValue.Format(object?)` is the shared invariant-culture formatter in `HR.Application.Engines.Finance.Export` (confirmed present). If a QuestPDF Fluent method name differs in 2026.6.0, adapt to the version's API (see the existing `DocumentRenderer.cs` for the exact QuestPDF surface in use) — keep the output a valid PDF byte[].

- [ ] **Step 5: Register in DI** — in `DependencyInjection.cs`, near the report service registrations:
```csharp
services.AddSingleton<HR.Application.Engines.Finance.Export.IExportWriter, HR.Modules.Platform.Services.Reports.PdfExportWriter>();
```
(Registering as an additional `IExportWriter` singleton makes it resolvable through `IEnumerable<IExportWriter>` alongside the Payroll-registered Excel/CSV/Txt/Xml writers.)

- [ ] **Step 6: Run to verify it passes** — filter `~PdfExportWriterTests` → PASS.

- [ ] **Step 7: Commit**
```bash
git add backend/src/HR.Application/Engines/Finance/Export/TabularDataset.cs backend/src/HR.Modules/Platform/Services/Reports/PdfExportWriter.cs backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs backend/tests/HR.Modules.Platform.Tests/Reports/PdfExportWriterTests.cs
git commit -m "feat(reports): PDF export writer (QuestPDF) + ExportFormat.Pdf + DI"
```

---

## Task E4: `ReportExportService` (run → flatten → writer) + DI

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/Reports/IReportExportService.cs` + `ReportExportService.cs`
- Modify: `backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/ReportExportServiceTests.cs`

**Interfaces:**
- Consumes: `IReportExecutionService.RunForExportAsync` (E1), `ReportResultFlattener.Flatten` (E2), `IEnumerable<IExportWriter>`, `IReportAccessService.EnsureCanReadAsync`, `ApplicationDbContext` (to read the report's name for the file title/name).
- Produces:
  - `record ReportExportFile(byte[] Content, string ContentType, string FileName);`
  - `interface IReportExportService { Task<ReportExportFile> ExportAsync(Guid reportId, ExportFormat format, CancellationToken ct); }`
  - Behavior: `EnsureCanReadAsync` first; run for export; flatten to `TabularDataset` (title = report `NameEn`); pick `writers.FirstOrDefault(w => w.Format == format)` or throw `ValidationException` if unsupported; `FileName = $"{codeOrName}-{yyyyMMdd?}.{writer.Extension}"` (no time API in services beyond `DateTime.UtcNow` — allowed here). Return `new ReportExportFile(writer.Write(dataset), writer.ContentType, fileName)`.

- [ ] **Step 1: Write the failing test** (`[SkippableFact]`, mirror the harness; seed a tiny report; export as CSV; assert bytes contain the header text). Copy `StubUser`/`Conn`. Construct the service with a real `ReportExecutionService`, `ReportAccessService`, and the real writers (`new CsvExportWriter()` etc. from `HR.Application.Engines.Finance.Export`, plus `new PdfExportWriter()`):
```csharp
[SkippableFact]
public async Task Exports_report_as_csv_bytes()
{
    Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
    // ... seed ObjectDefinition(Employee) + one Employee + a ReportDefinition (primary=Employee,
    //     one visible field "BasicSalary") exactly like ReportExecutionIntegrationTests but simpler (no join/group).
    // Build: catalog, resolver, exec = new ReportExecutionService(db, user, resolver);
    // access = new ReportAccessService(db, user);
    // writers = new IExportWriter[] { new CsvExportWriter(), new PdfExportWriter() };
    // var svc = new ReportExportService(db, exec, access, writers);
    // var file = await svc.ExportAsync(reportId, ExportFormat.Csv, default);
    // file.ContentType.Should().Be("text/csv");
    // file.FileName.Should().EndWith(".csv");
    // System.Text.Encoding.UTF8.GetString(file.Content).Should().Contain("Basic Salary"); // header label
}
```
(Keep the seed minimal but real — the test must actually run the report and produce non-empty CSV. Reuse the seeding shape from `ReportExecutionIntegrationTests.cs`.)

- [ ] **Step 2: Run to verify it fails** — filter `~ReportExportServiceTests` → locally Skipped; compile-fail is the RED gate (service missing).

- [ ] **Step 3: Implement** `IReportExportService.cs` + `ReportExportService.cs`:
```csharp
// IReportExportService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using HR.Application.Engines.Finance.Export;

namespace HR.Modules.Platform.Services.Reports;

public sealed record ReportExportFile(byte[] Content, string ContentType, string FileName);

public interface IReportExportService
{
    Task<ReportExportFile> ExportAsync(Guid reportId, ExportFormat format, CancellationToken ct);
}
```
```csharp
// ReportExportService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Finance.Export;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Reports;

public sealed class ReportExportService : IReportExportService
{
    private readonly ApplicationDbContext _db;
    private readonly IReportExecutionService _exec;
    private readonly IReportAccessService _access;
    private readonly IEnumerable<IExportWriter> _writers;

    public ReportExportService(ApplicationDbContext db, IReportExecutionService exec, IReportAccessService access, IEnumerable<IExportWriter> writers)
    { _db = db; _exec = exec; _access = access; _writers = writers; }

    public async Task<ReportExportFile> ExportAsync(Guid reportId, ExportFormat format, CancellationToken ct)
    {
        await _access.EnsureCanReadAsync(reportId, ct);

        var writer = _writers.FirstOrDefault(w => w.Format == format)
            ?? throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("format", $"Unsupported export format '{format}'.") });

        var meta = await _db.Set<ReportDefinition>().Where(r => r.Id == reportId)
            .Select(r => new { r.NameEn, r.Code }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("ReportDefinition", reportId);

        var result = await _exec.RunForExportAsync(reportId, ct);
        var dataset = ReportResultFlattener.Flatten(result, meta.NameEn);
        var bytes = writer.Write(dataset);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd");
        var safe = string.IsNullOrWhiteSpace(meta.Code) ? "report" : meta.Code;
        var fileName = $"{safe}-{stamp}.{writer.Extension}";
        return new ReportExportFile(bytes, writer.ContentType, fileName);
    }
}
```

- [ ] **Step 4: Register in DI** — in `DependencyInjection.cs`:
```csharp
services.AddScoped<HR.Modules.Platform.Services.Reports.IReportExportService, HR.Modules.Platform.Services.Reports.ReportExportService>();
```

- [ ] **Step 5: Build + test** — `dotnet build backend/src/HR.Api/HR.Api.csproj` (0 errors); `dotnet test backend/tests/HR.Modules.Platform.Tests` (green; export test skipped locally).

- [ ] **Step 6: Commit**
```bash
git add backend/src/HR.Modules/Platform/Services/Reports/IReportExportService.cs backend/src/HR.Modules/Platform/Services/Reports/ReportExportService.cs backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs backend/tests/HR.Modules.Platform.Tests/Reports/ReportExportServiceTests.cs
git commit -m "feat(reports): export service (run-for-export -> flatten -> writer, access-gated)"
```

---

## Task E5: Export endpoint

**Files:**
- Create: `backend/src/HR.Modules/Platform/Queries/Reports/ReportExportQueries.cs`
- Modify: `backend/src/HR.Modules/Platform/Controllers/ReportsController.cs`

**Interfaces:**
- Produces: `record ExportReportQuery(Guid Id, string Format) : IRequest<ReportExportFile>` + handler (parses `Format` string → `ExportFormat` case-insensitive, default→`ValidationException`); `GET {id}/export?format=` returns `File(...)`.

- [ ] **Step 1: Write the query + handler** in `ReportExportQueries.cs`:
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Finance.Export;
using HR.Modules.Platform.Services.Reports;
using MediatR;

namespace HR.Modules.Platform.Queries.Reports;

public record ExportReportQuery(Guid Id, string Format) : IRequest<ReportExportFile>;

public class ExportReportQueryHandler : IRequestHandler<ExportReportQuery, ReportExportFile>
{
    private readonly IReportExportService _export;
    public ExportReportQueryHandler(IReportExportService export) => _export = export;

    public Task<ReportExportFile> Handle(ExportReportQuery request, CancellationToken ct)
    {
        if (!Enum.TryParse<ExportFormat>(request.Format, ignoreCase: true, out var fmt))
            throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("format", $"Unknown export format '{request.Format}'. Use excel, csv, or pdf.") });
        return _export.ExportAsync(request.Id, fmt, ct);
    }
}
```

- [ ] **Step 2: Add the endpoint** in `ReportsController.cs` (near `{id}/run`):
```csharp
[HttpGet("{id:guid}/export")]
[RequirePermission("Platform.Reports.Export")]
public async Task<IActionResult> Export(Guid id, [FromQuery] string format = "excel", CancellationToken ct = default)
{
    var file = await Mediator.Send(new ExportReportQuery(id, format), ct);
    return File(file.Content, file.ContentType, file.FileName);
}
```
Add `using HR.Modules.Platform.Queries.Reports;` if not present. Note this returns a raw `File(...)` (not the `ApiResponse<T>` envelope) — correct for binary downloads, matching the payslip/employee-export controllers.

- [ ] **Step 3: Build** — `dotnet build backend/src/HR.Api/HR.Api.csproj` (0 errors).

- [ ] **Step 4: Commit**
```bash
git add backend/src/HR.Modules/Platform/Queries/Reports/ReportExportQueries.cs backend/src/HR.Modules/Platform/Controllers/ReportsController.cs
git commit -m "feat(reports): GET {id}/export?format=excel|csv|pdf endpoint"
```

---

## Final verification
- [ ] `dotnet test backend/tests/HR.Modules.Platform.Tests` → all green (DB-touching skipped locally).
- [ ] `dotnet build backend/src/HR.Api/HR.Api.csproj` → 0 errors.
- [ ] Deploy: merge to main, zip-deploy API (no migration in this increment — the `ExportFormat.Pdf` enum change is code-only). Verify `GET /api/platform/reports/{id}/export?format=csv` returns 401 unauthenticated (registered + auth-gated) in Swagger.

## Self-Review notes (author)
- Spec §7 `{id}/export?format=excel|csv` ✓ (+pdf from R2, brought forward since QuestPDF is already available). Uses the seeded `Platform.Reports.Export` permission.
- Hardening (Phase-2 follow-ups): tag dup-name → 400, tag-assign existence → 404 (H1). The recent-view ordering "refactor" is intentionally omitted — it is functionally correct already (cosmetic only).
- Export runs the FULL result up to `RowCap` (E1), not a 200-row page — `Truncated` still surfaces when the cap is hit (the future viewer/export UI must warn).
- No project reference to `HR.Modules.Payroll`; writers resolved via `IEnumerable<IExportWriter>`.
