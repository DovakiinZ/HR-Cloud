# Dashboard Widget PDF/XLSX Export (SP-3a) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add real server-side Excel + PDF export of a widget's data, reusing the `IExportWriter` engine built for reports.

**Architecture:** A pure `WidgetResultFlattener` turns a `WidgetDataResult` (scalar/series/table) into the export engine's `TabularDataset`; a `WidgetExportService` executes the saved widget, flattens, and picks a writer by `ExportFormat` from `IEnumerable<IExportWriter>`; a `GET {widgetId}/export` endpoint streams the file; the frontend adds Excel/PDF buttons. No DB migration.

**Tech Stack:** .NET 8, EF Core 8, xUnit + FluentAssertions; Next.js 16.2.6 + TypeScript. Reuses `HR.Application.Engines.Finance.Export` (Excel/Csv/Pdf writers, `TabularDataset`, `ExportValue`).

## Global Constraints

- **No DB migration.** Reuse existing entities/services.
- **Reuse the export engine — do NOT fork, do NOT add a `HR.Modules.Payroll` project ref.** Writers resolve at runtime via `IEnumerable<IExportWriter>` (`w => w.Format == format`). Types: `ExportFormat { Excel=1, Csv=2, Txt=3, Xml=4, Pdf=5 }`; `TabularDataset(string Title, IReadOnlyList<TabularColumn> Columns, IReadOnlyList<IReadOnlyDictionary<string,object?>> Rows)`; `TabularColumn(string Key, string Header, TabularAlign Align = Start, int? Width = null)`; `TabularAlign { Start, End, Center }`; `ExportValue.Format(object?)`. All in `HR.Application.Engines.Finance.Export`.
- **Widget execution:** `IWidgetDataService.ExecuteWidgetAsync(Guid widgetId, IReadOnlyList<WidgetFilterSpec>? dashboardFilters, CancellationToken ct) → WidgetDataResult`. Title source: `_db.DashboardWidgets` (typed DbSet already used by `WidgetDataService`), entity `HR.Modules.Dashboards.Entities.DashboardWidget : TenantEntity` with property `Name` (tenant-filtered automatically in-request).
- `WidgetDataResult = { Kind: "scalar"|"series"|"table", Value?: double, Series: SeriesPoint[]{Key,Label,Value}, Columns: TableColumn[]{Code,Label,Type}, Rows: List<Dictionary<string,object?>> }`.
- Exceptions: `HR.Application.Common.Exceptions.{ValidationException,NotFoundException}`; `ValidationException` has no string ctor — use `new ValidationException(new[]{ new FluentValidation.Results.ValidationFailure("field","msg") })`.
- Endpoint gated `[RequirePermission("Platform.Dashboards.View")]`; returns raw `File(...)`.
- DB-touching tests are `[SkippableFact]` gated on env `REPORTS_TEST_DB`. Pure logic gets DB-free tests. **Gates:** `dotnet build backend/src/HR.Api/HR.Api.csproj` = 0 errors; `dotnet test backend/tests/HR.Modules.Platform.Tests` green; `npx next build` = 0 errors. Commit after each task.

---

## Task 1: `WidgetResultFlattener` (pure)

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/WidgetData/WidgetResultFlattener.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Dashboards/WidgetResultFlattenerTests.cs`

**Interfaces:**
- Consumes: `WidgetDataResult`, `SeriesPoint`, `TableColumn` (`HR.Modules.Platform.Services.WidgetData`); `TabularDataset`, `TabularColumn`, `TabularAlign` (`HR.Application.Engines.Finance.Export`).
- Produces: `public static TabularDataset WidgetResultFlattener.Flatten(WidgetDataResult result, string title)`.

- [ ] **Step 1: Write the failing tests** `WidgetResultFlattenerTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using HR.Application.Engines.Finance.Export;
using HR.Modules.Platform.Services.WidgetData;
using Xunit;

namespace HR.Modules.Platform.Tests.Dashboards;

public class WidgetResultFlattenerTests
{
    [Fact]
    public void Scalar_flattens_to_one_cell()
    {
        var ds = WidgetResultFlattener.Flatten(new WidgetDataResult { Kind = "scalar", Value = 42 }, "KPI");
        ds.Columns.Select(c => c.Key).Should().Equal("value");
        ds.Rows.Should().HaveCount(1);
        ds.Rows[0]["value"].Should().Be(42.0);
    }

    [Fact]
    public void Series_flattens_to_label_value_rows()
    {
        var result = new WidgetDataResult { Kind = "series", Series = new()
            { new SeriesPoint { Key = "hr", Label = "HR", Value = 3 }, new SeriesPoint { Key = "it", Label = "IT", Value = 5 } } };
        var ds = WidgetResultFlattener.Flatten(result, "By Dept");
        ds.Columns.Select(c => c.Key).Should().Equal("label", "value");
        ds.Rows.Should().HaveCount(2);
        ds.Rows[1]["label"].Should().Be("IT");
        ds.Rows[1]["value"].Should().Be(5.0);
    }

    [Fact]
    public void Table_flattens_columns_and_rows()
    {
        var result = new WidgetDataResult
        {
            Kind = "table",
            Columns = new() { new TableColumn { Code = "Name", Label = "Name", Type = "Text" }, new TableColumn { Code = "Salary", Label = "Salary", Type = "Currency" } },
            Rows = new() { new Dictionary<string, object?> { ["Name"] = "Ali", ["Salary"] = 5000 } },
        };
        var ds = WidgetResultFlattener.Flatten(result, "T");
        ds.Columns.Select(c => c.Key).Should().Equal("Name", "Salary");
        ds.Columns.Single(c => c.Key == "Salary").Align.Should().Be(TabularAlign.End);
        ds.Rows[0]["Name"].Should().Be("Ali");
        ds.Rows[0]["Salary"].Should().Be(5000);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~WidgetResultFlattenerTests` → FAIL (type missing).

- [ ] **Step 3: Implement** `WidgetResultFlattener.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using HR.Application.Engines.Finance.Export;

namespace HR.Modules.Platform.Services.WidgetData;

/// <summary>Pure projection of an executed widget result into the tabular export payload.
/// scalar → one Value cell; series → Label/Value rows; table → the result's own columns/rows.</summary>
public static class WidgetResultFlattener
{
    private static readonly HashSet<string> NumericTypes = new(System.StringComparer.OrdinalIgnoreCase)
        { "Number", "Decimal", "Currency", "Percentage", "Int", "Integer", "Double", "Float", "Money" };

    public static TabularDataset Flatten(WidgetDataResult result, string title)
    {
        switch (result.Kind)
        {
            case "scalar":
            {
                var cols = new List<TabularColumn> { new("value", "Value", TabularAlign.End) };
                var rows = new List<IReadOnlyDictionary<string, object?>>
                    { new Dictionary<string, object?> { ["value"] = result.Value } };
                return new TabularDataset(title, cols, rows);
            }
            case "series":
            {
                var cols = new List<TabularColumn> { new("label", "Label"), new("value", "Value", TabularAlign.End) };
                var rows = result.Series
                    .Select(p => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["label"] = p.Label, ["value"] = p.Value })
                    .ToList();
                return new TabularDataset(title, cols, rows);
            }
            default: // table
            {
                var cols = result.Columns
                    .Select(c => new TabularColumn(c.Code, c.Label, NumericTypes.Contains(c.Type) ? TabularAlign.End : TabularAlign.Start))
                    .ToList();
                var rows = result.Rows
                    .Select(r =>
                    {
                        var d = new Dictionary<string, object?>();
                        foreach (var c in result.Columns) d[c.Code] = r.TryGetValue(c.Code, out var v) ? v : null;
                        return (IReadOnlyDictionary<string, object?>)d;
                    })
                    .ToList();
                return new TabularDataset(title, cols, rows);
            }
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes** — same filter → PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/WidgetData/WidgetResultFlattener.cs backend/tests/HR.Modules.Platform.Tests/Dashboards/WidgetResultFlattenerTests.cs
git commit -m "feat(dashboards): pure WidgetDataResult -> TabularDataset flattener"
```

---

## Task 2: `WidgetExportService` + DI

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/WidgetData/IWidgetExportService.cs` + `WidgetExportService.cs`
- Modify: `backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Dashboards/WidgetExportServiceTests.cs`

**Interfaces:**
- Consumes: `IWidgetDataService.ExecuteWidgetAsync` (Task's engine), `WidgetResultFlattener.Flatten` (Task 1), `IEnumerable<IExportWriter>`, `ApplicationDbContext` (`_db.DashboardWidgets`).
- Produces: `record WidgetExportFile(byte[] Content, string ContentType, string FileName)`; `interface IWidgetExportService { Task<WidgetExportFile> ExportAsync(Guid widgetId, ExportFormat format, CancellationToken ct); }`.

- [ ] **Step 1: Create `IWidgetExportService.cs`:**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using HR.Application.Engines.Finance.Export;

namespace HR.Modules.Platform.Services.WidgetData;

public sealed record WidgetExportFile(byte[] Content, string ContentType, string FileName);

public interface IWidgetExportService
{
    Task<WidgetExportFile> ExportAsync(Guid widgetId, ExportFormat format, CancellationToken ct);
}
```

- [ ] **Step 2: Create `WidgetExportService.cs`:**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Finance.Export;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.WidgetData;

public sealed class WidgetExportService : IWidgetExportService
{
    private readonly IWidgetDataService _data;
    private readonly IEnumerable<IExportWriter> _writers;
    private readonly ApplicationDbContext _db;

    public WidgetExportService(IWidgetDataService data, IEnumerable<IExportWriter> writers, ApplicationDbContext db)
    { _data = data; _writers = writers; _db = db; }

    public async Task<WidgetExportFile> ExportAsync(Guid widgetId, ExportFormat format, CancellationToken ct)
    {
        var writer = _writers.FirstOrDefault(w => w.Format == format)
            ?? throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("format", $"Unsupported export format '{format}'.") });

        var name = await _db.DashboardWidgets.Where(w => w.Id == widgetId).Select(w => w.Name).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("DashboardWidget", widgetId);

        var result = await _data.ExecuteWidgetAsync(widgetId, null, ct);
        var dataset = WidgetResultFlattener.Flatten(result, name);
        var bytes = writer.Write(dataset);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd");
        var safe = string.IsNullOrWhiteSpace(name) ? "widget" : System.Text.RegularExpressions.Regex.Replace(name, "[\\\\/:*?\"<>|]+", "_");
        return new WidgetExportFile(bytes, writer.ContentType, $"{safe}-{stamp}.{writer.Extension}");
    }
}
```

> Confirm `_db.DashboardWidgets` is the DbSet name (it is — `WidgetDataService.cs` uses `_db.DashboardWidgets`) and `DashboardWidget.Name` exists. If the widget row is tenant-filtered out for the caller, the `FirstOrDefaultAsync` returns null → `NotFoundException` (correct — a caller can't export a widget they can't see).

- [ ] **Step 3: Write a `[SkippableFact]`** `WidgetExportServiceTests.cs` — seed a `DashboardWidget` with a minimal runnable spec (mirror the seeding used by any existing widget/dashboard test, or the reports export test harness `Conn`/`StubUser`), export as `ExportFormat.Excel`, assert `Content` non-empty and `ContentType`/`FileName` end with the Excel extension. Gate with `Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.")`. If seeding a runnable widget is impractical, assert instead that an unknown format throws `ValidationException` (DB-free) and note the e2e was deferred — the flattener DB-free tests are the required coverage.

- [ ] **Step 4: Register in DI** — in `DependencyInjection.cs`, near the widget-data service registration:

```csharp
services.AddScoped<HR.Modules.Platform.Services.WidgetData.IWidgetExportService, HR.Modules.Platform.Services.WidgetData.WidgetExportService>();
```

- [ ] **Step 5: Build + test** — `dotnet build backend/src/HR.Api/HR.Api.csproj` (0 errors); `dotnet test backend/tests/HR.Modules.Platform.Tests` (green; e2e skipped locally).

- [ ] **Step 6: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/WidgetData/IWidgetExportService.cs backend/src/HR.Modules/Platform/Services/WidgetData/WidgetExportService.cs backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs backend/tests/HR.Modules.Platform.Tests/Dashboards/WidgetExportServiceTests.cs
git commit -m "feat(dashboards): widget export service (execute -> flatten -> writer, access via widget-data)"
```

---

## Task 3: Export endpoint

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Controllers/WidgetDataController.cs`

**Interfaces:**
- Consumes: `IWidgetExportService.ExportAsync`, `ExportFormat`.
- Produces: `GET api/platform/dashboards/widget-data/{widgetId}/export?format=excel|csv|pdf` returning `File(...)`.

- [ ] **Step 1: Inject the service + add the endpoint** in `WidgetDataController.cs`. Add `using HR.Application.Engines.Finance.Export;` and a field:

```csharp
    private readonly IWidgetExportService _export;
    public WidgetDataController(IWidgetDataService data, IWidgetSuggestionService suggest, IWidgetExportService export)
    {
        _data = data; _suggest = suggest; _export = export;
    }
```
(Update the existing constructor to add the `export` parameter/assignment — keep `_data`/`_suggest` as-is.)

Then add:
```csharp
    /// <summary>Export a saved widget's data as a real Excel/PDF/CSV file.</summary>
    [HttpGet("{widgetId:guid}/export")]
    [RequirePermission("Platform.Dashboards.View")]
    public async Task<IActionResult> Export(Guid widgetId, [FromQuery] string format = "excel", CancellationToken ct = default)
    {
        if (!Enum.TryParse<ExportFormat>(format, ignoreCase: true, out var fmt))
            return BadRequest(ApiResponse.Fail($"Unknown export format '{format}'. Use excel, csv, or pdf."));
        var file = await _export.ExportAsync(widgetId, fmt, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }
```

> `ApiResponse.Fail` is already used in this codebase's controllers (e.g. `ObjectCatalogController`). `Enum.TryParse<ExportFormat>` accepts "excel"/"csv"/"pdf" (case-insensitive, matching enum member names Excel/Csv/Pdf); "xlsx" is NOT a member — the FE sends "excel".

- [ ] **Step 2: Build** — `dotnet build backend/src/HR.Api/HR.Api.csproj` → 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/HR.Modules/Platform/Controllers/WidgetDataController.cs
git commit -m "feat(dashboards): GET widget-data/{id}/export?format=excel|csv|pdf endpoint"
```

---

## Task 4: Frontend — export helper + widget-card buttons

**Files:**
- Modify: `src/lib/api/dashboards.ts`
- Modify: `src/components/dashboard/widget-card.tsx`

**Interfaces:**
- Consumes: `API_BASE_URL`, `getAccessToken`.
- Produces: `exportWidget(widgetId, format, fallbackName?)`.

- [ ] **Step 1: Add `exportWidget`** to `src/lib/api/dashboards.ts` (mirrors the reports `exportReport` blob-download). First confirm the file imports (or add) `API_BASE_URL` from `"../api-client"` and `getAccessToken` from `"../auth-storage"` and `toast` from `"sonner"`:

```typescript
export type WidgetExportFormat = "excel" | "pdf" | "csv";
const WIDGET_EXT: Record<WidgetExportFormat, string> = { excel: "xlsx", pdf: "pdf", csv: "csv" };

/** Download a widget's data as a real file (server-side Excel/PDF/CSV). Streams raw bytes. */
export async function exportWidget(widgetId: string, format: WidgetExportFormat, fallbackName = "widget"): Promise<void> {
  const token = getAccessToken();
  const res = await fetch(`${API_BASE_URL}/api/platform/dashboards/widget-data/${widgetId}/export?format=${format}`, {
    method: "GET",
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}) },
  });
  if (res.status === 401) { toast.error("انتهت الجلسة. يرجى تسجيل الدخول من جديد"); throw new Error("Unauthorized"); }
  if (res.status === 403) { toast.error("ليس لديك صلاحية لتصدير هذه الودجة"); throw new Error("Forbidden"); }
  if (!res.ok) { toast.error("تعذر تصدير الودجة"); throw new Error(`Export failed (${res.status})`); }

  let filename = `${fallbackName}-${new Date().toISOString().slice(0, 10)}.${WIDGET_EXT[format]}`;
  const cd = res.headers.get("Content-Disposition");
  const match = cd?.match(/filename\*?=(?:UTF-8''|")?([^";]+)/i);
  if (match?.[1]) filename = decodeURIComponent(match[1].replace(/"/g, ""));

  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url; a.download = filename;
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
```
> If `dashboards.ts` does not already import `API_BASE_URL`/`getAccessToken`/`toast`, add those imports (the reports client `src/lib/api/reports.ts` imports them the same way: `import { apiFetch, API_BASE_URL } from "../api-client"; import { getAccessToken } from "../auth-storage"; import { toast } from "sonner";`).

- [ ] **Step 2: Add Excel/PDF buttons** to the export menu in `src/components/dashboard/widget-card.tsx`. Open the file and find the export `menu` block (it renders CSV + PNG buttons around line 96-104, using `exportCsv`/`exportPng`). Import `exportWidget` from `@/lib/api/dashboards` and, INSIDE the same menu dropdown, add two buttons BEFORE or AFTER the CSV entry — but only when the widget has a persisted id (server export needs a saved widget). Use the card's existing widget id prop (find how the component receives the widget — a `widgetId`/`widget.id`/`id` prop; use that; if the card is the unsaved builder preview it will lack a real id — guard with `widgetId &&`):

```tsx
{widgetId && (
  <>
    <button onClick={() => { setMenu(false); exportWidget(widgetId, "excel", titleAr); }} className="flex w-full items-center gap-2 px-3 py-1.5 hover:bg-muted">
      <FileSpreadsheet className="h-3.5 w-3.5" /> Excel
    </button>
    <button onClick={() => { setMenu(false); exportWidget(widgetId, "pdf", titleAr); }} className="flex w-full items-center gap-2 px-3 py-1.5 hover:bg-muted">
      <Download className="h-3.5 w-3.5" /> PDF
    </button>
  </>
)}
```
`FileSpreadsheet` and `Download` are already imported in `widget-card.tsx`. Use the component's real widget-id variable name (verify by reading the props/destructure at the top of the file — the spec assumes a `widgetId` string is available; if it is named differently, e.g. `id` or `widget.id`, use that and note it). Keep the existing CSV/PNG/print buttons unchanged.

- [ ] **Step 3: Build** — `npx next build` → 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/lib/api/dashboards.ts src/components/dashboard/widget-card.tsx
git commit -m "feat(dashboards): widget-card Excel/PDF export buttons (saved widgets)"
```

---

## Final verification & deploy
- [ ] `dotnet build backend/src/HR.Api/HR.Api.csproj` → 0 errors; `dotnet test backend/tests/HR.Modules.Platform.Tests` → green.
- [ ] `npx next build` → 0 errors.
- [ ] Deploy backend once: `dotnet publish backend/src/HR.Api -c Release -o ./publish`, zip forward-slash entries (Python `zipfile`), `az webapp deploy --resource-group HR --name hrcloud-api-v4xd --src-path <zip> --type zip`. Push → Vercel auto-deploys FE. No migration.
- [ ] Live-verify: `GET /api/platform/dashboards/widget-data/{id}/export?format=excel` → 401 unauth; authenticated → a real xlsx for a scalar KPI, a series chart, and a table widget; `format=pdf` → a valid `%PDF`.

## Self-review notes (author)
- Spec Component 1 (flattener) → Task 1; Component 2 (service+DI) → Task 2; Component 3 (endpoint) → Task 3; Component 4 (FE) → Task 4. All covered.
- No migration; reuses `IExportWriter`/`TabularDataset`/`ExportValue` via `IEnumerable<IExportWriter>`; no Payroll project ref.
- Type consistency: `WidgetExportFile(byte[],string,string)`; `ExportFormat` members Excel/Csv/Pdf; flattener keys `value`/`label`/column codes; `_db.DashboardWidgets`/`DashboardWidget.Name` verified against `WidgetDataService`.
- Known limits (carried): per-widget only (no composite dashboard PDF); exports aggregated data not chart image; per-widget `RequiredPermission` enforced inside `ExecuteWidgetAsync`.
