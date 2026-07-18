# Widget Drill-down Details + Export — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`).

**Goal:** Click a KPI value / table row → detail drawer of the underlying records; export those rows to PDF/Excel/CSV. Reuses the existing drill-down + export engines. No engine change, no migration.

**Architecture:** FE makes the scalar KPI + raw-table rows clickable (they already feed `WidgetCard`'s drill-down drawer). BE adds `WidgetExportService.ExportRowsAsync` (GetRowsAsync → Flatten → IExportWriter) + a `drilldown/export` endpoint. FE adds an `exportDrilldown` blob helper + PDF/Excel/CSV buttons in the drawer.

**Tech Stack:** .NET 8 (xUnit + FluentAssertions), Next.js 16 TSX. No FE test framework (build-verified).

## Global Constraints
- No engine change. Reuse `IWidgetDataService.GetRowsAsync`, `WidgetResultFlattener.Flatten`, `IExportWriter`, the existing `DrilldownDrawer`/`drilldownWidget`.
- Excel + PDF widget export already work — DO NOT add writer DI registrations (all writers are already in the shared container via `AddPayrollModule`).
- Permission gate for the new endpoint: `Platform.Dashboards.View` (mirror the existing drilldown/export actions).
- FE RTL Arabic, existing Tailwind tokens; `next build` stays green.
- Bar/pie/grouped-table drill-down already works — don't touch it. Line/Trend/Area stay non-clickable.

## Confirmed facts
- `WidgetExportService.ExportAsync(Guid widgetId, ExportFormat format, CancellationToken ct)` pattern: `writer = _writers.FirstOrDefault(w => w.Format == format) ?? throw new ValidationException(...)`; `WidgetResultFlattener.Flatten(result, name)`; `writer.Write(dataset)`; returns `new WidgetExportFile(bytes, writer.ContentType, $"{safe}-{stamp}.{writer.Extension}")`. `IExportWriter` has `.Format` (ExportFormat), `.Write(TabularDataset)`, `.ContentType`, `.Extension`.
- `ExportFormat` enum (`HR.Application.Engines.Finance.Export`): Excel=1, Csv=2, Txt=3, Xml=4, Pdf=5.
- `IWidgetDataService.GetRowsAsync(WidgetQuerySpec spec, string? segmentKey, IReadOnlyList<WidgetFilterSpec>? dashboardFilters, int page, int pageSize, CancellationToken ct) : Task<WidgetDataResult>`.
- `WidgetDataController` (route `api/platform/dashboards/widget-data`, `BaseApiController`, `OkResponse`) ctor injects `IWidgetDataService _data, IWidgetSuggestionService _suggest, IWidgetExportService _export, IMetricWidgetService, ICurrentUserService`. Existing `Export` action: `Enum.TryParse<ExportFormat>(format, ignoreCase:true, out var fmt)` else `BadRequest(ApiResponse.Fail(...))`; then `File(file.Content, file.ContentType, file.FileName)` (confirm the WidgetExportFile property names by reading the record — likely `Content`/`ContentType`/`FileName`; use whatever `ExportAsync`'s action uses).
- FE: `widget-renderer.tsx` `RendererProps { type, result, onSelect?(point: SeriesPoint) }`; scalar branch renders a `<span>` (no onClick); raw `kind==="table"` branch renders `<tr>` (no onClick). `SeriesPoint { key: string|null; label: string; value: number }` (confirm shape). `WidgetCard.onSelect = (p) => { if (enableDrilldown && !editMode) setDrill({ key: p.key, label: p.label }); }`. `DrilldownDrawer` props `{ open, title, spec, segmentKey, segmentLabel?, dashboardFilters?, onClose }`, calls `drilldownWidget(spec, segmentKey, dashboardFilters, page, pageSize)`, renders `data.columns`/`data.rows`. `exportWidget(widgetId, format, fallbackName)` in `dashboards.ts` is the blob-download pattern to mirror (raw fetch + auth + object URL).

---

## Task 1: WidgetExportService.ExportRowsAsync (TDD)

**Files:** Modify `backend/src/HR.Modules/Platform/Services/WidgetData/IWidgetExportService.cs`, `WidgetExportService.cs`; Test `backend/tests/HR.Modules.Platform.Tests/WidgetData/WidgetExportRowsTests.cs`.

**Interfaces:**
- Produces: `IWidgetExportService.ExportRowsAsync(WidgetQuerySpec spec, string? segmentKey, IReadOnlyList<WidgetFilterSpec>? dashboardFilters, ExportFormat format, string title, CancellationToken ct) : Task<WidgetExportFile>`.

- [ ] **Step 1: Write the failing test.** First OPEN `WidgetExportService.cs`, `IWidgetExportService.cs`, `WidgetExportFile` (its record props), `WidgetResultFlattener`, `IExportWriter`, and `WidgetDataResult` to get exact shapes; then write:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HR.Application.Engines.Finance.Export;
using HR.Modules.Platform.Services.WidgetData;
using Xunit;

namespace HR.Modules.Platform.Tests.WidgetData;

public class WidgetExportRowsTests
{
    private sealed class FakeWidgetData : IWidgetDataService
    {
        public WidgetQuerySpec? LastSpec; public string? LastSegment; public int LastPageSize;
        public Task<WidgetDataResult> GetRowsAsync(WidgetQuerySpec spec, string? segmentKey,
            IReadOnlyList<WidgetFilterSpec>? df, int page, int pageSize, CancellationToken ct)
        {
            LastSpec = spec; LastSegment = segmentKey; LastPageSize = pageSize;
            return Task.FromResult(new WidgetDataResult
            {
                Kind = "table",
                Columns = new() { new TableColumn { Key = "name", Label = "Name" } },
                Rows = new() { new Dictionary<string, object?> { ["name"] = "Ali" } },
                TotalCount = 1,
            });
        }
        public Task<WidgetDataResult> ExecuteAsync(WidgetQuerySpec s, IReadOnlyList<WidgetFilterSpec>? d, CancellationToken c) => throw new NotImplementedException();
        public Task<WidgetDataResult> ExecuteWidgetAsync(Guid id, IReadOnlyList<WidgetFilterSpec>? d, CancellationToken c) => throw new NotImplementedException();
    }

    private sealed class FakeWriter : IExportWriter
    {
        public ExportFormat Format => ExportFormat.Excel;
        public string ContentType => "application/xlsx";
        public string Extension => "xlsx";
        public TabularDataset? Written;
        public byte[] Write(TabularDataset dataset) { Written = dataset; return new byte[] { 1, 2, 3 }; }
    }

    private static WidgetQuerySpec Spec() => new() { ObjectCode = "Employee", Aggregation = "Count" };

    [Fact]
    public async Task ExportRows_runs_GetRows_flattens_and_writes()
    {
        var data = new FakeWidgetData();
        var writer = new FakeWriter();
        var sut = new WidgetExportService(data, new IExportWriter[] { writer }, db: null!);

        var file = await sut.ExportRowsAsync(Spec(), segmentKey: "3", dashboardFilters: null,
            format: ExportFormat.Excel, title: "Employees", ct: default);

        data.LastSegment.Should().Be("3");
        data.LastPageSize.Should().Be(5000);            // MaxExportRows
        writer.Written.Should().NotBeNull();
        file.Content.Should().BeEquivalentTo(new byte[] { 1, 2, 3 });   // adapt to WidgetExportFile prop name
        file.ContentType.Should().Be("application/xlsx");
        file.FileName.Should().Contain("Employees").And.EndWith(".xlsx");
    }

    [Fact]
    public async Task ExportRows_unsupported_format_throws()
    {
        var sut = new WidgetExportService(new FakeWidgetData(), Array.Empty<IExportWriter>(), db: null!);
        await FluentActions.Invoking(() => sut.ExportRowsAsync(Spec(), null, null, ExportFormat.Pdf, "t", default))
            .Should().ThrowAsync<HR.Application.Common.Exceptions.ValidationException>();
    }
}
```
> Adapt `WidgetDataResult`/`TableColumn`/`WidgetExportFile` construction + property names (`Content` vs `Bytes`, etc.) and the `IWidgetDataService`/`IExportWriter` member list to the REAL types you read. The fake must implement every interface member.

- [ ] **Step 2: Run to verify FAIL** — `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~WidgetExportRowsTests` → FAIL (method missing).

- [ ] **Step 3: Implement.** Add to `IWidgetExportService`:
```csharp
Task<WidgetExportFile> ExportRowsAsync(WidgetQuerySpec spec, string? segmentKey,
    IReadOnlyList<WidgetFilterSpec>? dashboardFilters, ExportFormat format, string title, CancellationToken ct);
```
Add to `WidgetExportService` (mirror `ExportAsync`'s writer-select + filename logic):
```csharp
private const int MaxExportRows = 5000;

public async Task<WidgetExportFile> ExportRowsAsync(WidgetQuerySpec spec, string? segmentKey,
    IReadOnlyList<WidgetFilterSpec>? dashboardFilters, ExportFormat format, string title, CancellationToken ct)
{
    var writer = _writers.FirstOrDefault(w => w.Format == format)
        ?? throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("format", $"Unsupported export format '{format}'.") });

    var result = await _data.GetRowsAsync(spec, segmentKey, dashboardFilters, 1, MaxExportRows, ct);
    var name = string.IsNullOrWhiteSpace(title) ? "details" : title;
    var dataset = WidgetResultFlattener.Flatten(result, name);
    var bytes = writer.Write(dataset);

    var stamp = DateTime.UtcNow.ToString("yyyyMMdd");
    var safe = System.Text.RegularExpressions.Regex.Replace(name, "[\\\\/:*?\"<>|]+", "_");
    return new WidgetExportFile(bytes, writer.ContentType, $"{safe}-{stamp}.{writer.Extension}");
}
```
> Match the real `WidgetExportFile` ctor/property order.

- [ ] **Step 4: Run to verify PASS** (same filter). Expected: both tests pass.
- [ ] **Step 5: Commit** `git add backend/src/HR.Modules/Platform/Services/WidgetData/IWidgetExportService.cs backend/src/HR.Modules/Platform/Services/WidgetData/WidgetExportService.cs backend/tests/HR.Modules.Platform.Tests/WidgetData/WidgetExportRowsTests.cs && git commit -m "feat(dashboards): WidgetExportService.ExportRowsAsync (drill-down rows export)"`

---

## Task 2: drilldown/export endpoint

**Files:** Modify `backend/src/HR.Modules/Platform/Controllers/WidgetDataController.cs`.

- [ ] **Step 1: Add the action** (mirror the existing `Export` action's format-parse + `File(...)` return exactly; `_export` is already injected):
```csharp
public sealed record DrilldownExportRequest(WidgetQuerySpec Spec, string? SegmentKey, List<WidgetFilterSpec>? DashboardFilters, string? Title);

/// <summary>Export the drill-down detail rows behind a widget value as Excel/PDF/CSV.</summary>
[HttpPost("drilldown/export")]
[RequirePermission("Platform.Dashboards.View")]
public async Task<IActionResult> DrilldownExport([FromBody] DrilldownExportRequest req, [FromQuery] string format = "excel", CancellationToken ct = default)
{
    if (!Enum.TryParse<ExportFormat>(format, ignoreCase: true, out var fmt))
        return BadRequest(ApiResponse.Fail($"Unknown export format '{format}'. Use excel, csv, or pdf."));
    var file = await _export.ExportRowsAsync(req.Spec, req.SegmentKey, req.DashboardFilters, fmt, req.Title ?? "details", ct);
    return File(file.Content, file.ContentType, file.FileName); // match the Export action's property names
}
```
> Use the SAME `WidgetExportFile` property names the existing `Export` action uses in its `File(...)` call.

- [ ] **Step 2: Build** `dotnet build backend/src/HR.Api/HR.Api.csproj -v q` → 0 errors.
- [ ] **Step 3: Commit** `git add backend/src/HR.Modules/Platform/Controllers/WidgetDataController.cs && git commit -m "feat(dashboards): POST widget-data/drilldown/export endpoint"`

---

## Task 3: Backend build + test gate
- [ ] `dotnet build backend/HR.sln -v q` → 0 errors; `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --nologo` → all pass. Commit if incidental fix.

---

## Task 4: Clickable KPI + table rows (FE)

**Files:** Modify `src/components/dashboard/widget-renderer.tsx`.

- [ ] **Step 1:** OPEN `widget-renderer.tsx`. In the **scalar** branch (`result.kind === "scalar"`), make the value clickable ONLY when `onSelect` is provided: wrap/annotate the number element with `role="button"`, `className` add `cursor-pointer hover:opacity-80` when `onSelect`, and `onClick={() => onSelect?.({ key: null, label: "" })}`. In the raw **table** branch (`result.kind === "table"`), add to each `<tr>`: `className="cursor-pointer hover:bg-muted/50"` and `onClick={() => onSelect?.({ key: null, label: String(Object.values(row)[0] ?? "") })}` (drills into all rows for the raw table; if the spec has a groupBy the grouped-series path is used instead and is unchanged). Use the real `SeriesPoint` shape you read; if `key` must be a string, use `null as unknown` per the existing type or widen. Do NOT change bar/pie/series-table/leaderboard branches.
- [ ] **Step 2:** `npx next build` → 0 type errors.
- [ ] **Step 3: Commit** `git add src/components/dashboard/widget-renderer.tsx && git commit -m "feat(dashboards): make KPI value + raw table rows drill-down clickable"`

---

## Task 5: exportDrilldown client + drawer buttons (FE)

**Files:** Modify `src/lib/api/dashboards.ts`, `src/components/dashboard/drilldown-drawer.tsx`.

- [ ] **Step 1:** In `dashboards.ts`, add `exportDrilldown` mirroring the existing `exportWidget` blob-download helper (raw `fetch` to `API_BASE_URL`, `Authorization: Bearer <getAccessToken()>`, POST JSON body, read blob, create object URL, click a temp `<a>` with the `Content-Disposition` filename or a fallback). Signature:
```ts
export async function exportDrilldown(
  spec: WidgetQuerySpec, segmentKey: string | null, format: "excel" | "pdf" | "csv",
  dashboardFilters?: WidgetFilterSpec[], title = "details",
): Promise<void>
// POST /api/platform/dashboards/widget-data/drilldown/export?format={format}  body { spec, segmentKey, dashboardFilters, title }
```
Reuse the exact auth + blob logic from `exportWidget` (copy its body, change the URL/method/body).
- [ ] **Step 2:** In `DrilldownDrawer`, add a small button row in the header: **Excel / PDF / CSV**, each calling `exportDrilldown(spec, segmentKey, "excel"|"pdf"|"csv", dashboardFilters, title)`; disable while a request is in-flight; `toast` on error (sonner). Match the drawer's existing styling.
- [ ] **Step 3:** `npx next build` → green.
- [ ] **Step 4: Commit** `git add src/lib/api/dashboards.ts src/components/dashboard/drilldown-drawer.tsx && git commit -m "feat(dashboards): export drill-down detail rows (Excel/PDF/CSV) from the drawer"`

---

## Task 6: Final FE build gate
- [ ] `npx next build` → compiled, 0 type errors. Commit if incidental.

---

## Self-Review
- Click KPI/table → detail drawer → Tasks 4 (clickable) + existing drawer. ✅
- Export detail rows PDF/Excel/CSV → Tasks 1,2 (BE) + 5 (FE). ✅
- No engine change / reuse GetRowsAsync+Flatten+writers → Tasks 1,2. ✅
- No DI writer change (Excel already works) → Global Constraints. ✅
- Backend TDD; FE build-verified → Tasks 1,3,4,5,6. ✅
- Line/Trend/Area drill-down + widget-level export untouched → Non-goals. ✅

**Type consistency:** `ExportRowsAsync(spec, segmentKey, dashboardFilters, format, title, ct)` identical across Tasks 1↔2. `exportDrilldown(spec, segmentKey, format, dashboardFilters?, title?)` ↔ endpoint body `{ Spec, SegmentKey, DashboardFilters, Title }` (Task 2↔5). `onSelect?.({ key, label })` matches `SeriesPoint`/`WidgetCard.onSelect` (Task 4).
