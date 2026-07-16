# Dashboard Widget PDF/XLSX Export (SP-3a) — Design

**Date:** 2026-07-17
**Status:** Approved (design)
**Part of program:** Dashboards backlog (SP-3a widget export → SP-3b widget formula engine → SP-3c ESS dashboard → SP-3d heatmap/calendar).

## Context / current state (verified 2026-07-17)

The object-driven Dashboard Platform executes a widget to a `WidgetDataResult`; the only export today is **client-side CSV + PNG** (`src/lib/dashboard-export.ts`). This sub-project adds **real server-side Excel + PDF** files, reusing the `IExportWriter` engine built for reports. **No DB migration.**

Verified surfaces the design reuses:
- `IWidgetDataService.ExecuteWidgetAsync(Guid widgetId, IReadOnlyList<WidgetFilterSpec>? dashboardFilters, CancellationToken ct) → WidgetDataResult`. `WidgetDataResult = { Kind: "scalar"|"series"|"table", Value?: double, Series: SeriesPoint[]{Key,Label,Value}, Columns: TableColumn[]{Code,Label,Type}, Rows: Dictionary<string,object?>[], ... }`.
- Export engine (`HR.Application.Engines.Finance.Export`): `IExportWriter { ExportFormat Format; string ContentType; string Extension; byte[] Write(TabularDataset, ExportWriteOptions? = null); }`; `ExportFormat { Excel=1, Csv=2, Txt=3, Xml=4, Pdf=5 }`; `TabularDataset(string Title, IReadOnlyList<TabularColumn> Columns, IReadOnlyList<IReadOnlyDictionary<string,object?>> Rows)`; `TabularColumn(string Key, string Header, TabularAlign Align = Start, int? Width = null)`; `ExportValue.Format(object?)` invariant formatter. Writers are resolved at runtime via `IEnumerable<IExportWriter>` (Excel registered by Payroll DI; Csv/Txt/Xml/Pdf available; the reports `PdfExportWriter` is registered in Platform DI). This is exactly how `ReportExportService` resolves writers — **do NOT add a project ref to `HR.Modules.Payroll`.**
- `WidgetDataController` (`api/platform/dashboards/widget-data`) endpoints are gated `[RequirePermission("Platform.Dashboards.View")]`. The widget spec may carry an optional `RequiredPermission`; enforcement of that lives inside `ExecuteWidgetAsync` (reused as-is — this design does not re-implement it).

Design principle: mirror the reports export path (`run → flatten → writer by format`), reuse the writers, no fork, no migration.

---

## Component 1 — `WidgetResultFlattener` (pure)

**File:** `backend/src/HR.Modules/Platform/Services/WidgetData/WidgetResultFlattener.cs`

`public static TabularDataset Flatten(WidgetDataResult result, string title)`:
- **scalar** (`Kind == "scalar"`): columns = `[ TabularColumn("value", "Value", TabularAlign.End) ]`; rows = one dict `{ ["value"] = result.Value }`.
- **series** (`Kind == "series"`): columns = `[ TabularColumn("label","Label"), TabularColumn("value","Value", TabularAlign.End) ]`; rows = one per `SeriesPoint` → `{ ["label"] = p.Label, ["value"] = p.Value }`.
- **table** (else): columns = one `TabularColumn(c.Code, c.Label, c.Type is numeric ? End : Start)` per `result.Columns`; rows = `result.Rows` projected `{ [c.Code] = row.GetValueOrDefault(c.Code) }`. (Numeric type check: `Type` in {"Number","Decimal","Currency","Percentage","Int","Integer","Double"} case-insensitive → `TabularAlign.End`.)

DB-free unit tests for all three shapes.

## Component 2 — `IWidgetExportService` / `WidgetExportService`

**Files:** `backend/src/HR.Modules/Platform/Services/WidgetData/IWidgetExportService.cs` + `WidgetExportService.cs`; DI registration in `backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs`.

- `sealed record WidgetExportFile(byte[] Content, string ContentType, string FileName);`
- `interface IWidgetExportService { Task<WidgetExportFile> ExportAsync(Guid widgetId, ExportFormat format, CancellationToken ct); }`
- `WidgetExportService(IWidgetDataService data, IEnumerable<IExportWriter> writers, ApplicationDbContext db)`:
  - Resolve writer = `writers.FirstOrDefault(w => w.Format == format)` or throw `ValidationException("format", $"Unsupported export format '{format}'.")`.
  - Read the widget's display title from `DashboardWidget` (id → `Title`/`NameEn`, whichever the entity has; fall back to `"widget"`); if the widget row doesn't exist → `NotFoundException("DashboardWidget", widgetId)`.
  - `var result = await data.ExecuteWidgetAsync(widgetId, null, ct);`
  - `var dataset = WidgetResultFlattener.Flatten(result, title);`
  - `var bytes = writer.Write(dataset);`
  - `FileName = $"{safeTitleOrCode}-{DateTime.UtcNow:yyyyMMdd}.{writer.Extension}";` return `new WidgetExportFile(bytes, writer.ContentType, fileName)`.
  - Register `services.AddScoped<IWidgetExportService, WidgetExportService>();`.

`[SkippableFact]` (gated `REPORTS_TEST_DB`, mirroring the reports test harness) end-to-end: seed a minimal `DashboardWidget` with a runnable spec, export as `Excel`, assert non-empty bytes + content type. (Pure logic covered by Component 1's DB-free tests.)

## Component 3 — Export endpoint

**File:** `backend/src/HR.Modules/Platform/Controllers/WidgetDataController.cs` (add one endpoint + inject `IWidgetExportService`).

```csharp
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
Inject `IWidgetExportService _export` via the constructor. Returns a raw `File(...)` (not the JSON envelope), matching the reports export + payslip controllers.

## Component 4 — Frontend

**Files:** `src/lib/api/dashboards.ts` (+ export fn), `src/components/dashboard/widget-card.tsx` (+ buttons).

- `exportWidget(widgetId: string, format: "excel"|"pdf"|"csv", fallbackName?: string): Promise<void>` — authed blob download reusing the reports `exportReport` pattern (fetch with Bearer, read Content-Disposition filename, trigger download; handle 401/403/!ok toasts). `EXT = { excel: "xlsx", pdf: "pdf", csv: "csv" }`; URL `${API_BASE_URL}/api/platform/dashboards/widget-data/${widgetId}/export?format=${format}`.
- In `widget-card.tsx`, add **Excel** and **PDF** entries to the existing export menu (which has CSV/PNG/print). Server export needs a **saved** widget id — only show the Excel/PDF items when the card has a persisted `widget.id` (not the unsaved builder-preview state). Keep the existing client CSV/PNG/print as-is.

---

## Testing & gates
- **Backend:** `dotnet build backend/src/HR.Api/HR.Api.csproj` = 0 errors; `dotnet test backend/tests/HR.Modules.Platform.Tests` green (flattener DB-free tests pass; export `[SkippableFact]` skipped locally). TDD: write flattener tests first.
- **Frontend:** `npx next build` = 0 errors.
- **Deploy:** backend zip-deploy once, push → Vercel auto-deploys FE. No migration.
- Live-verify: `GET /api/platform/dashboards/widget-data/{id}/export?format=excel` returns 401 unauth; authenticated returns a real xlsx/pdf for a scalar, a series, and a table widget.

## Known limits carried forward
- Per-widget export only (a full-dashboard composite PDF of all widgets is a later increment).
- Series/scalar export the aggregated data, not the chart image (PNG already covers the chart image client-side).
- `RequiredPermission` on a widget spec is enforced by the existing `ExecuteWidgetAsync`; the export endpoint adds no additional per-widget gate beyond `Platform.Dashboards.View`.

## Self-review
- No placeholders; each component names its file, exact signatures, and behavior.
- Consistent: reuses `IExportWriter`/`TabularDataset`/`ExportValue` + the reports export pattern; no Payroll ref; no migration.
- Scope: one implementation plan (flattener + service + endpoint + FE). Formula engine / ESS / heatmap are separate sub-projects.
- Ambiguity resolved: three result kinds have explicit column/row mappings; writer resolved via `IEnumerable<IExportWriter>`; Excel/PDF buttons only for saved widgets.
