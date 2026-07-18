# Widget Drill-down Details + Export — Design

**Date:** 2026-07-18
**Status:** Approved (design presented + owner said proceed). Engine unchanged.
**Related:** [[dashboard-platform-engine]], [[dashboard-builder-redesign]]

## Goal

On the live dashboard, let the user **click a value to see the records behind it** and **export those records to PDF/Excel/CSV**:
- Click a KPI number (e.g. gross payroll `52K`) → a detail drawer listing the underlying rows (the employees + salaries).
- Click a table/department row → the employees under that segment.
- From the detail drawer, export the rows to PDF / Excel / CSV.

## What already exists (reuse, no engine change)

- **Drill-down engine:** `IWidgetDataService.GetRowsAsync(spec, segmentKey, dashboardFilters, page, pageSize)` returns a paginated detail table; `POST /widget-data/drilldown` + `drilldownWidget()` client + `DrilldownDrawer` are wired. Clicking a **bar/pie/grouped-table row already drills down.** Backend supports `segmentKey=null` (scalar KPI → all underlying rows).
- **Widget export engine:** `WidgetExportService.ExportAsync(widgetId, format)` (execute → `WidgetResultFlattener.Flatten` → `IExportWriter`). PDF **and Excel both work live** (verified http 200) — `AddPayrollModule()` registers Excel/Csv/Txt/Xml writers into the shared container alongside Platform's Pdf writer. **No DI fix needed.**

## Gaps to close

1. **Scalar KPI is not clickable** — `widget-renderer.tsx` renders the scalar as a plain `<span>` with no `onClick`; `onSelect` is never called. (Backend ready.)
2. **Raw `kind="table"` rows are not clickable** — the table branch has no row `onClick`.
3. **The detail drawer has no export** — once the underlying rows are shown, there's no way to export *those rows*.

## Design

### A. Clickable KPI + table rows (frontend only)
`widget-renderer.tsx`:
- Scalar branch: wrap the number so that when `onSelect` is provided (and thus drill-down is enabled), it's `cursor-pointer` and `onClick={() => onSelect({ key: null, label: "" })}`. `WidgetCard.onSelect` already opens the drawer; the drawer already handles `segmentKey=null`.
- Raw `kind="table"` branch: make each `<tr>` clickable → `onSelect({ key: <row group value or null>, label: <first cell> })`. When the raw table has no group dimension, pass `key:null` (drills into all rows).
No change to bar/pie/grouped-table (already working). Line/Trend/Area stay non-clickable (out of scope, as agreed).

### B. Drill-down rows export (backend)
Add to `IWidgetExportService` + `WidgetExportService`:
```
Task<WidgetExportFile> ExportRowsAsync(WidgetQuerySpec spec, string? segmentKey,
    IReadOnlyList<WidgetFilterSpec>? dashboardFilters, ExportFormat format, string title, CancellationToken ct)
```
- Select the writer by `format` (same as `ExportAsync`; 400 via `ValidationException` if unsupported).
- `result = await _data.GetRowsAsync(spec, segmentKey, dashboardFilters, page: 1, pageSize: MaxExportRows, ct)` where `MaxExportRows = 5000` (a `const`; log/accept truncation beyond it — detail exports are bounded).
- `dataset = WidgetResultFlattener.Flatten(result, title)`; `bytes = writer.Write(dataset)`; return `WidgetExportFile(bytes, writer.ContentType, "<safeTitle>-<yyyyMMdd>.<ext>")` (mirror `ExportAsync`'s filename logic).

New endpoint on `WidgetDataController`:
```
POST /api/platform/dashboards/widget-data/drilldown/export?format=excel|pdf|csv
[RequirePermission("Platform.Dashboards.View")]
body: DrilldownExportRequest { WidgetQuerySpec Spec; string? SegmentKey; List<WidgetFilterSpec>? DashboardFilters; string? Title }
→ parse format (Enum.TryParse<ExportFormat> ignoreCase; 400 on unknown) → _export.ExportRowsAsync(...) → File(bytes, contentType, fileName)
```
Mirror the existing `Export` action's format-parse + `File(...)` return exactly.

### C. Detail-drawer export buttons (frontend)
`src/lib/api/dashboards.ts`: add `exportDrilldown(spec, segmentKey, format, dashboardFilters?, title?)` — a blob download (raw `fetch` with auth + `Content-Disposition` filename), mirroring the existing `exportWidget` blob-download helper.
`DrilldownDrawer`: add **PDF / Excel / CSV** buttons in the header that call `exportDrilldown(spec, segmentKey, format, dashboardFilters, title)`. (CSV can also be produced client-side from the already-loaded `data`, but routing all three through the endpoint keeps one code path.)

## Testing

**Backend (TDD):** `WidgetExportService.ExportRowsAsync` with a fake `IWidgetDataService` (returns a known table `WidgetDataResult`) + a fake `IExportWriter`: asserts it calls `GetRowsAsync` with the given spec/segmentKey and `pageSize=MaxExportRows`, flattens with the title, selects the writer by format, returns the bytes + a `<title>-<date>.<ext>` filename; unsupported format → `ValidationException`. Solution build + Platform tests green.

**Frontend:** `npx next build` green; manual verification (click KPI/row → drawer → export).

## Non-goals / deferred
- Line/Trend/Area chart point drill-down.
- Changing the existing widget-level export (already works).
- New widget types.
