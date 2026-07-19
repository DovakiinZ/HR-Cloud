# Dashboard Builder Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** A business-concept-first dashboard widget builder (Widget Type → Business Data → Metric → Filters → Save) consuming the Semantic Catalog, with a server-side metric→widget bridge, keeping the existing technical builder as "Advanced". Engine unchanged.

**Architecture:** Two new backend endpoints materialize a chosen catalog metric into the existing `WidgetQuerySpec` server-side (via the existing `MetricSpecMapper` + `IWidgetDataService` + `AddDashboardWidgetCommand`), so the new FE builder speaks only metric-code + friendly filters + visualization. A new `business-widget-builder.tsx` drives the 5-step flow; the old `widget-builder.tsx` stays untouched as Advanced.

**Tech Stack:** .NET 8 (xUnit + FluentAssertions), Next.js 16.2.6 (TSX, Tailwind, RTL Arabic). No new deps, no migration, no FE test framework (build-verified).

## Global Constraints

- **No engine change.** Reuse `MetricSpecMapper.ToWidgetSpec` (public, `HR.Modules.Platform.Services.SemanticCatalog`), `IWidgetDataService.ExecuteAsync(spec, dashboardFilters, ct)`, and the existing `AddDashboardWidgetCommand`. Do not modify `WidgetDataService`/`WidgetQuerySpec`/catalog.
- **FE never builds a `WidgetQuerySpec`.** It holds `{ metricCode, filters: WidgetFilterSpec[], visualization, dateGranularity }`. Materialization is server-side.
- **The Semantic Catalog contract stays semantic.** The new bridge lives in the dashboards/widget-data controllers, not the catalog controller.
- Permission gates: preview `Platform.Dashboards.View`; create-from-metric `Platform.Dashboards.Edit` (mirror the existing add-widget gate).
- Caller permissions come from `ICurrentUserService.Permissions`; build `CatalogQueryContext(_user.Permissions)`.
- FE: RTL Arabic, match existing builder styling (`h-9 border border-border bg-background px-2 text-sm`, terracotta/beige tokens). `next build` must stay green.
- Keep `widget-builder.tsx` (Advanced) unmodified.

## Confirmed facts (from exploration)
- `AddDashboardWidgetCommand : IRequest<DashboardWidgetDto>` fields: `DashboardDefinitionId, WidgetType (enum), TitleEn, TitleAr, Configuration (string? JSON), SortOrder, Layout (WidgetLayoutInput?)`. Handler stores `Configuration` verbatim.
- `WidgetType` enum: KpiCard=1, Table=2, BarChart=3, LineChart=4, PieChart=5, DonutChart=6, TrendChart=7, ProgressWidget=8, ActivityFeed=9, CalendarWidget=10.
- `WidgetQuerySpec` (class, `HR.Modules.Platform.Services.WidgetData`): `ObjectCode, Aggregation, AggregationField?, GroupByField?, DateGranularity?, Visualization?, Limit?, RequiredPermission?, Filters (List<WidgetFilterSpec>), Formula?, Measures (List<WidgetMeasureSpec>)`. `WidgetFilterSpec { Field, Operator="eq", Value? }`.
- `ISemanticCatalogProvider.GetMetric(CatalogQueryContext, string) : SemanticMetric?` (null on missing/permission). `SemanticMetric.Definition : SemanticMetricDefinition`; `.DefaultVisualization`, `.RequiredPermissions`, `.SuggestedFilterFields`.
- `NotFoundException` exists (used as `throw new NotFoundException("X", id)` and `new NotFoundException(string)`). MediatR is registered; inject `ISender`/`IMediator`.
- FE: `apiFetch<T>(path, {method, body})` from `src/lib/api-client.ts` (auth + envelope-unwrap automatic). Existing `widget-renderer.tsx` renders a `WidgetDataResult`. `WidgetDataResult` TS shape in `src/types/dashboard.ts`. Existing builder saves via `widgetPayloadFromSpec` → `addWidget`; the NEW builder saves via the new `addWidgetFromMetric`.

---

## File map
**Create (backend):** `HR.Modules/Platform/Services/WidgetData/IMetricWidgetService.cs`, `MetricWidgetService.cs`; tests `HR.Modules.Platform.Tests/WidgetData/MetricWidgetServiceTests.cs`.
**Modify (backend):** `WidgetDataController.cs` (+preview-metric), `DashboardsController.cs` (+from-metric), `DependencyInjection.cs` (register service).
**Create (FE):** `src/lib/api/catalog.ts`, `src/components/dashboard/business-widget-builder.tsx`.
**Modify (FE):** `src/lib/api/dashboards.ts` (+previewMetric, +addWidgetFromMetric), `src/app/(dashboard)/dashboard/builder/page.tsx`, `src/app/(dashboard)/dashboard/page.tsx` (default to business builder + Advanced toggle).

---

## Task 1: MetricWidgetService — spec building (TDD)

**Files:** Create `backend/src/HR.Modules/Platform/Services/WidgetData/IMetricWidgetService.cs`, `MetricWidgetService.cs`; Test `backend/tests/HR.Modules.Platform.Tests/WidgetData/MetricWidgetServiceTests.cs`.

**Interfaces:**
- Consumes: `ISemanticCatalogProvider`, `CatalogQueryContext`, `SemanticMetric*` (HR.Application); `MetricSpecMapper` (Platform); `WidgetQuerySpec`/`WidgetFilterSpec` (Platform.WidgetData); `IWidgetDataService`; `ISender` (MediatR); `NotFoundException`.
- Produces: `IMetricWidgetService` with `BuildSpec(...)`, `WidgetTypeFor(string?)`, `PreviewAsync(...)`, `CreateWidgetAsync(...)`.

- [ ] **Step 1: Write the failing test** (covers the pure pieces `BuildSpec` + `WidgetTypeFor`; `Preview/Create` are thin wrappers verified by build + later manual)

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using HR.Application.SemanticCatalog;
using HR.Application.SemanticCatalog.Contracts;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.WidgetData;
using Xunit;

namespace HR.Modules.Platform.Tests.WidgetData;

public class MetricWidgetServiceTests
{
    private static readonly DateTime Now = new(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FakeCatalog : ISemanticCatalogProvider
    {
        private readonly SemanticMetric? _m;
        public FakeCatalog(SemanticMetric? m) => _m = m;
        public SemanticMetric? GetMetric(CatalogQueryContext ctx, string code) => code == _m?.Code ? _m : null;
        public IReadOnlyList<SemanticDomain> GetDomains(CatalogQueryContext c) => Array.Empty<SemanticDomain>();
        public IReadOnlyList<SemanticObject> GetObjects(CatalogQueryContext c, string? d = null) => Array.Empty<SemanticObject>();
        public SemanticObject? GetObject(CatalogQueryContext c, string code) => null;
        public IReadOnlyList<SemanticMetric> GetMetrics(CatalogQueryContext c, string? d = null) => Array.Empty<SemanticMetric>();
        public IReadOnlyList<SemanticSearchHit> Search(CatalogQueryContext c, string q) => Array.Empty<SemanticSearchHit>();
        public CatalogHealth GetHealth() => new(0,0,0,0, Array.Empty<HiddenItem>());
    }

    private static SemanticMetric Metric(string agg = "Count", string? field = null, string viz = "KpiCard",
        params SemanticMetricFilter[] baked)
        => new("m1","اسم","Name","وصف","Desc","Icon","employees", new[]{"Employees.View"},
            new SemanticMetricDefinition("Employee", agg, field, baked, null), viz, new[]{"DepartmentId"});

    private static MetricWidgetService Sut(SemanticMetric? m)
        => new(new FakeCatalog(m), widgetData: null!, sender: null!);

    private static CatalogQueryContext Ctx => new(new[] { "Employees.View" });

    [Fact]
    public void BuildSpec_maps_metric_and_sets_visualization_default()
    {
        var spec = Sut(Metric(viz: "BarChart")).BuildSpec(Ctx, "m1", Array.Empty<WidgetFilterSpec>(), null, null, Now);
        spec.ObjectCode.Should().Be("Employee");
        spec.Aggregation.Should().Be("Count");
        spec.Visualization.Should().Be("BarChart");   // from metric.DefaultVisualization
        spec.Limit.Should().Be(12);
    }

    [Fact]
    public void BuildSpec_visualization_override_wins()
    {
        var spec = Sut(Metric(viz: "BarChart")).BuildSpec(Ctx, "m1", Array.Empty<WidgetFilterSpec>(), "Table", "month", Now);
        spec.Visualization.Should().Be("Table");
        spec.DateGranularity.Should().Be("month");
    }

    [Fact]
    public void BuildSpec_appends_user_filters_after_baked()
    {
        var baked = new SemanticMetricFilter("Status", "Equals", Value: "1");
        var user = new WidgetFilterSpec { Field = "DepartmentId", Operator = "eq", Value = "abc" };
        var spec = Sut(Metric(baked: baked)).BuildSpec(Ctx, "m1", new[] { user }, null, null, Now);
        spec.Filters.Should().HaveCount(2);
        spec.Filters[0].Field.Should().Be("Status");        // baked first
        spec.Filters[1].Field.Should().Be("DepartmentId");  // user after
    }

    [Fact]
    public void BuildSpec_throws_NotFound_when_metric_missing_or_denied()
    {
        FluentActions.Invoking(() => Sut(Metric()).BuildSpec(Ctx, "nope", Array.Empty<WidgetFilterSpec>(), null, null, Now))
            .Should().Throw<HR.Application.Common.Exceptions.NotFoundException>();
    }

    [Theory]
    [InlineData("KpiCard", WidgetType.KpiCard)]
    [InlineData("Gauge", WidgetType.KpiCard)]
    [InlineData("BarChart", WidgetType.BarChart)]
    [InlineData("HorizontalBar", WidgetType.BarChart)]
    [InlineData("LineChart", WidgetType.LineChart)]
    [InlineData("PieChart", WidgetType.PieChart)]
    [InlineData("DonutChart", WidgetType.DonutChart)]
    [InlineData("Table", WidgetType.Table)]
    [InlineData("Leaderboard", WidgetType.Table)]
    [InlineData("something-unknown", WidgetType.KpiCard)]
    public void WidgetTypeFor_maps(string viz, WidgetType expected)
        => MetricWidgetService.WidgetTypeFor(viz).Should().Be(expected);
}
```

> Confirm the exact namespace of `NotFoundException` (grep `class NotFoundException`); adjust the `using`/fully-qualified name in the test + service to match (the plan assumes `HR.Application.Common.Exceptions`). Confirm `ISemanticCatalogProvider`'s exact method list to make the fake compile (copy from `ISemanticCatalogProvider.cs`).

- [ ] **Step 2: Run to verify FAIL** — `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~MetricWidgetServiceTests` → FAIL (service not defined).

- [ ] **Step 3: Implement the interface + service**

`IMetricWidgetService.cs`:
```csharp
using HR.Application.SemanticCatalog;
using HR.Modules.Platform.Services.WidgetData; // WidgetQuerySpec/WidgetFilterSpec live here

namespace HR.Modules.Platform.Services.WidgetData;

public interface IMetricWidgetService
{
    WidgetQuerySpec BuildSpec(CatalogQueryContext ctx, string metricCode,
        IReadOnlyList<WidgetFilterSpec> userFilters, string? visualization, string? dateGranularity, DateTime nowUtc);
    Task<WidgetDataResult> PreviewAsync(CatalogQueryContext ctx, string metricCode,
        IReadOnlyList<WidgetFilterSpec> userFilters, string? visualization, string? dateGranularity, CancellationToken ct);
    Task<DashboardWidgetDto> CreateWidgetAsync(Guid dashboardId, CatalogQueryContext ctx, string metricCode,
        IReadOnlyList<WidgetFilterSpec> userFilters, string? visualization, string? dateGranularity,
        string titleAr, string titleEn, WidgetLayoutInput? layout, CancellationToken ct);
}
```
> `CreateWidgetAsync` returns the `DashboardWidgetDto` from `AddDashboardWidgetCommand`; type it as that DTO (find its exact name/namespace — `DashboardWidgetDto`) rather than `object`. `WidgetLayoutInput` is the type used by `AddDashboardWidgetCommand.Layout`.

`MetricWidgetService.cs`:
```csharp
using HR.Application.Common.Exceptions;   // adjust to the real NotFoundException namespace
using HR.Application.SemanticCatalog;
using HR.Domain.Enums;
using HR.Modules.Platform.Commands.Dashboards; // AddDashboardWidgetCommand + WidgetLayoutInput + DashboardWidgetDto (adjust)
using HR.Modules.Platform.Services.SemanticCatalog; // MetricSpecMapper
using MediatR;
using System.Text.Json;

namespace HR.Modules.Platform.Services.WidgetData;

public sealed class MetricWidgetService : IMetricWidgetService
{
    private readonly ISemanticCatalogProvider _catalog;
    private readonly IWidgetDataService _widgetData;
    private readonly ISender _sender;
    public MetricWidgetService(ISemanticCatalogProvider catalog, IWidgetDataService widgetData, ISender sender)
    { _catalog = catalog; _widgetData = widgetData; _sender = sender; }

    public WidgetQuerySpec BuildSpec(CatalogQueryContext ctx, string metricCode,
        IReadOnlyList<WidgetFilterSpec> userFilters, string? visualization, string? dateGranularity, DateTime nowUtc)
    {
        var metric = _catalog.GetMetric(ctx, metricCode)
            ?? throw new NotFoundException("Metric", metricCode);
        var spec = MetricSpecMapper.ToWidgetSpec(metric.Definition, nowUtc);
        spec.Visualization = string.IsNullOrWhiteSpace(visualization) ? metric.DefaultVisualization : visualization;
        spec.DateGranularity = dateGranularity;
        spec.Limit ??= 12;
        spec.RequiredPermission = metric.RequiredPermissions.FirstOrDefault();
        if (userFilters is { Count: > 0 }) spec.Filters.AddRange(userFilters);
        return spec;
    }

    public async Task<WidgetDataResult> PreviewAsync(CatalogQueryContext ctx, string metricCode,
        IReadOnlyList<WidgetFilterSpec> userFilters, string? visualization, string? dateGranularity, CancellationToken ct)
    {
        var spec = BuildSpec(ctx, metricCode, userFilters, visualization, dateGranularity, DateTime.UtcNow);
        return await _widgetData.ExecuteAsync(spec, null, ct);
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<DashboardWidgetDto> CreateWidgetAsync(Guid dashboardId, CatalogQueryContext ctx, string metricCode,
        IReadOnlyList<WidgetFilterSpec> userFilters, string? visualization, string? dateGranularity,
        string titleAr, string titleEn, WidgetLayoutInput? layout, CancellationToken ct)
    {
        var spec = BuildSpec(ctx, metricCode, userFilters, visualization, dateGranularity, DateTime.UtcNow);
        var viz = spec.Visualization;
        var configuration = JsonSerializer.Serialize(spec, JsonOpts); // camelCase; spec.Visualization is already inside
        var cmd = new AddDashboardWidgetCommand
        {
            DashboardDefinitionId = dashboardId,
            WidgetType = WidgetTypeFor(viz),
            TitleAr = titleAr, TitleEn = string.IsNullOrWhiteSpace(titleEn) ? titleAr : titleEn,
            Configuration = configuration, Layout = layout,
        };
        return await _sender.Send(cmd, ct);
    }

    // Configuration must serialize the spec's own fields PLUS a top-level "visualization" key (matches FE widgetPayloadFromSpec).
    // Simplest robust approach: serialize the spec to a JsonNode/dictionary and set visualization. If a helper record is
    // awkward, serialize `spec` then re-inject visualization. Implement whichever compiles cleanly; the stored JSON MUST
    // parse back to a WidgetQuerySpec (it already carries spec.Visualization, so serializing `spec` alone is sufficient
    // because WidgetQuerySpec.Visualization is a property — verify parseWidgetSpec on the FE reads spec.visualization).

    public static WidgetType WidgetTypeFor(string? visualization) => (visualization ?? "") switch
    {
        "Table" or "Leaderboard" => WidgetType.Table,
        "BarChart" or "HorizontalBar" => WidgetType.BarChart,
        "LineChart" or "AreaChart" or "TrendChart" => WidgetType.LineChart,
        "PieChart" => WidgetType.PieChart,
        "DonutChart" => WidgetType.DonutChart,
        _ => WidgetType.KpiCard, // KpiCard, Gauge, unknown
    };
}
```
> NOTE on Configuration: since `WidgetQuerySpec.Visualization` is already a property on the spec, `JsonSerializer.Serialize(spec)` yields a JSON object whose `visualization` field the FE `parseWidgetSpec` reads. Prefer `JsonSerializer.Serialize(spec)` and DELETE the `WidgetConfig` helper unless the FE requires a *duplicated* top-level visualization; verify against `parseWidgetSpec`/`widgetPayloadFromSpec`. Use camelCase JSON options consistent with the rest of the API (check how existing configs are serialized — the FE does `JSON.stringify({...spec, visualization})`, i.e. camelCase; ensure `JsonSerializer` uses camelCase, e.g. `new JsonSerializerOptions(JsonSerializerDefaults.Web)`).

- [ ] **Step 4: Run to verify PASS** (same filter). Expected: all `MetricWidgetServiceTests` pass.

- [ ] **Step 5: Commit** `git add backend/src/HR.Modules/Platform/Services/WidgetData/IMetricWidgetService.cs backend/src/HR.Modules/Platform/Services/WidgetData/MetricWidgetService.cs backend/tests/HR.Modules.Platform.Tests/WidgetData/MetricWidgetServiceTests.cs && git commit -m "feat(dashboards): MetricWidgetService (metric -> widget spec bridge)"`

---

## Task 2: Controller endpoints + DI

**Files:** Modify `WidgetDataController.cs`, `DashboardsController.cs`, `DependencyInjection.cs`.

- [ ] **Step 1: Register the service** in `AddPlatformModule` (near the widget-data registration): `services.AddScoped<HR.Modules.Platform.Services.WidgetData.IMetricWidgetService, HR.Modules.Platform.Services.WidgetData.MetricWidgetService>();`

- [ ] **Step 2: Add preview-metric to `WidgetDataController`.** The controller extends `BaseApiController` and uses the `OkResponse(...)` helper (NOT `Ok(ApiResponse...)`). Its ctor currently injects `(IWidgetDataService data, IWidgetSuggestionService suggest, IWidgetExportService export)`. Add `IMetricWidgetService metricWidgets` and `ICurrentUserService user` (namespace `HR.Application.Common.Interfaces`) to the ctor + fields. Add the action:
```csharp
public sealed record PreviewMetricRequest(string MetricCode, List<WidgetFilterSpec>? Filters, string? Visualization, string? DateGranularity);

[HttpPost("preview-metric")]
[RequirePermission("Platform.Dashboards.View")]
public async Task<ActionResult<ApiResponse<WidgetDataResult>>> PreviewMetric([FromBody] PreviewMetricRequest req, CancellationToken ct)
    => OkResponse(await _metricWidgets.PreviewAsync(
        new HR.Application.SemanticCatalog.CatalogQueryContext(_user.Permissions),
        req.MetricCode, req.Filters ?? new(), req.Visualization, req.DateGranularity, ct));
```

- [ ] **Step 3: Add from-metric to `DashboardsController`.** Open the controller — confirm it extends `BaseApiController` and use its `OkResponse(...)` helper (mirror its existing add-widget action's return style). Add `IMetricWidgetService` + `ICurrentUserService` to the ctor + fields (whatever it already injects, append these). `WidgetFilterSpec`/`WidgetLayoutInput`/`DashboardWidgetDto` usings: `HR.Modules.Platform.Services.WidgetData` / `HR.Modules.Platform.Commands.Dashboards` / `HR.Modules.Platform.DTOs.Dashboards`. If `CreateWidgetAsync` returns `DashboardWidgetDto` (recommended — type it so in Task 1), drop the cast.
```csharp
public sealed record CreateWidgetFromMetricRequest(string MetricCode, List<WidgetFilterSpec>? Filters,
    string? Visualization, string? DateGranularity, string TitleAr, string TitleEn, WidgetLayoutInput? Layout);

[HttpPost("{id:guid}/widgets/from-metric")]
[RequirePermission("Platform.Dashboards.Edit")]
public async Task<ActionResult<ApiResponse<DashboardWidgetDto>>> CreateWidgetFromMetric(Guid id, [FromBody] CreateWidgetFromMetricRequest req, CancellationToken ct)
    => OkResponse(await _metricWidgets.CreateWidgetAsync(id,
        new HR.Application.SemanticCatalog.CatalogQueryContext(_user.Permissions),
        req.MetricCode, req.Filters ?? new(), req.Visualization, req.DateGranularity, req.TitleAr, req.TitleEn, req.Layout, ct));
```

- [ ] **Step 4: Build the API** `dotnet build backend/src/HR.Api/HR.Api.csproj -v q` → 0 errors. Fix any `ApiResponse`/using mismatch against the existing actions.

- [ ] **Step 5: Commit** `git add backend/src/HR.Modules/Platform/Controllers/WidgetDataController.cs backend/src/HR.Modules/Platform/Controllers/DashboardsController.cs backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs && git commit -m "feat(dashboards): preview-metric + from-metric endpoints + DI"`

---

## Task 3: Full backend build + test gate

- [ ] **Step 1:** `dotnet build backend/HR.sln -v q` → 0 errors.
- [ ] **Step 2:** `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --nologo` → all pass (incl. new MetricWidgetServiceTests), skipped unchanged.
- [ ] **Step 3:** commit if any incidental fix.

---

## Task 4: Frontend catalog client

**Files:** Create `src/lib/api/catalog.ts`.

- [ ] **Step 1: Create the client** (mirror `dashboards.ts` style — `import { apiFetch } from "../api-client"`):
```ts
import { apiFetch } from "../api-client";

export interface SemanticDomain { code: string; nameAr: string; nameEn: string; descriptionAr: string; descriptionEn: string; icon: string; sortOrder: number; }
export interface SemanticFieldGroup { code: string; nameAr: string; nameEn: string; sortOrder: number; }
export interface SemanticField { objectCode: string; fieldCode: string; nameAr: string; nameEn: string; descriptionAr: string; descriptionEn: string; groupCode: string; icon?: string | null; keywords: string[]; role: string; defaultVisible: boolean; }
export interface SemanticFilter { fieldCode: string; nameAr: string; nameEn: string; controlType: string; referenceObjectCode?: string | null; }
export interface SemanticSort { fieldCode: string; direction: string; }
export interface SemanticObject { objectCode: string; domainCode: string; nameAr: string; nameEn: string; descriptionAr: string; descriptionEn: string; icon: string; keywords: string[]; defaultVisible: boolean; fieldGroups: SemanticFieldGroup[]; defaultSort?: SemanticSort | null; defaultFilters: SemanticFilter[]; recommendedMetricCodes: string[]; recommendedReportCodes: string[]; recommendedWidgetCodes: string[]; fields: SemanticField[]; }
export interface SemanticMetric { code: string; nameAr: string; nameEn: string; descriptionAr: string; descriptionEn: string; icon: string; domainCode: string; requiredPermissions: string[]; defaultVisualization: string; suggestedFilterFields: string[]; }

export const getDomains = () => apiFetch<SemanticDomain[]>("/api/platform/catalog/domains");
export const getCatalogObjects = (domain?: string) => apiFetch<SemanticObject[]>(`/api/platform/catalog/objects${domain ? `?domain=${encodeURIComponent(domain)}` : ""}`);
export const getCatalogObject = (code: string) => apiFetch<SemanticObject>(`/api/platform/catalog/objects/${encodeURIComponent(code)}`);
export const getMetrics = (domain?: string) => apiFetch<SemanticMetric[]>(`/api/platform/catalog/metrics${domain ? `?domain=${encodeURIComponent(domain)}` : ""}`);
export const getMetric = (code: string) => apiFetch<SemanticMetric>(`/api/platform/catalog/metrics/${encodeURIComponent(code)}`);
```
> The metric DTO intentionally does NOT include the definition (server-side only). Do not add it.

- [ ] **Step 2:** `npx next build` → compiles, 0 type errors.
- [ ] **Step 3: Commit** `git add src/lib/api/catalog.ts && git commit -m "feat(dashboards): Semantic Catalog frontend client"`

---

## Task 5: Frontend dashboards client — metric preview + create

**Files:** Modify `src/lib/api/dashboards.ts`.

- [ ] **Step 1: Add two functions** (near `previewWidgetData`/`addWidget`), reusing the existing `WidgetFilterSpec`/`WidgetDataResult`/`DashboardWidget` types:
```ts
export const previewMetric = (
  metricCode: string, filters: WidgetFilterSpec[], visualization?: string, dateGranularity?: string,
) => apiFetch<WidgetDataResult>("/api/platform/dashboards/widget-data/preview-metric", {
  method: "POST", body: { metricCode, filters, visualization, dateGranularity },
});

export const addWidgetFromMetric = (
  dashboardId: string,
  body: { metricCode: string; filters: WidgetFilterSpec[]; visualization?: string; dateGranularity?: string; titleAr: string; titleEn: string; layout?: { column: number; row: number; width: number; height: number } },
) => apiFetch<DashboardWidget>(`/api/platform/dashboards/${dashboardId}/widgets/from-metric`, { method: "POST", body });
```
> Confirm the exact import names of `WidgetFilterSpec`/`WidgetDataResult`/`DashboardWidget` already used in this file and reuse them.

- [ ] **Step 2:** `npx next build` → green.
- [ ] **Step 3: Commit** `git add src/lib/api/dashboards.ts && git commit -m "feat(dashboards): previewMetric + addWidgetFromMetric client fns"`

---

## Task 6: Business Widget Builder component

**Files:** Create `src/components/dashboard/business-widget-builder.tsx`.

**Interfaces:** Consumes `catalog.ts` (getDomains/getMetrics/getCatalogObject), `dashboards.ts` (previewMetric/addWidgetFromMetric), existing `widget-renderer.tsx` (renders a `WidgetDataResult`), `WidgetFilterSpec`/`WidgetDataResult` types.

Props:
```ts
export function BusinessWidgetBuilder({ dashboardId, onSaved, onCancel, onAdvanced }: {
  dashboardId: string; onSaved: () => void; onCancel: () => void; onAdvanced: () => void;
}): JSX.Element
```

- [ ] **Step 1: Build the component** — a 5-step RTL wizard matching the existing builder's structure/styling. Requirements:
  - Steps array (Arabic): `["نوع العنصر","البيانات","ما تريد عرضه","التصفية","الحفظ"]`.
  - **Step 0 Widget Type:** card grid of `{ viz, labelAr, icon }` for `KpiCard`(بطاقة مؤشر), `BarChart`(أعمدة), `LineChart`(خط بياني), `PieChart`(دائري), `DonutChart`(حلقي), `Table`(جدول). Sets `visualization`. Valid when set.
  - **Step 1 Business Data:** `getDomains()` on mount; card grid (icon + nameAr). Sets `domain`. Valid when set.
  - **Step 2 Metric:** on `domain` change, `getMetrics(domain)`; list rows (icon + nameAr + descriptionAr, selectable). Sets `metricCode` (+ keep the chosen `SemanticMetric` for its `suggestedFilterFields`/`defaultVisualization`). Empty-state text if none. Valid when set. When a metric is picked and the user hasn't overridden the type, you MAY pre-select the metric's `defaultVisualization` — but keep the user's Step-0 choice if they made one.
  - **Step 3 Filters (optional, always valid):** for the chosen metric, `getCatalogObject(objectCode)` — but the metric DTO has no objectCode. Instead: derive candidate filter fields from `metric.suggestedFilterFields`; to render friendly controls, fetch the domain's object via `getCatalogObjects(domain)` and match `suggestedFilterFields` against object `fields` (each `SemanticField` has `role`, and if it's an enum the option set is NOT in the semantic field — so for this phase render: date fields → a from/to `<input type=date>` pair producing `gte`/`lte` filters; all other suggested fields → an optional labeled text input producing an `eq` filter). Keep each filter row removable/optional. Output: `WidgetFilterSpec[]` (`{field, operator, value}`), only including rows the user filled. **Reference-picker dropdowns (Department/Branch by name) are out of scope this phase** — a text value input with the friendly label is acceptable; add a small helper caption. Check `src/components/dashboard/filter-bar.tsx` — IF it already has a reusable reference-option loader, reuse it for reference fields; otherwise text input.
  - **Step 4 Save:** `titleAr` input (required). On save: `addWidgetFromMetric(dashboardId, { metricCode, filters, visualization, dateGranularity: undefined, titleAr, titleEn: titleAr })` → toast success → `onSaved()`. Handle error via toast.
  - **Live preview panel (right column):** whenever `metricCode` is set, debounced (~400ms) call `previewMetric(metricCode, filters, visualization)` and render the result through the existing `widget-renderer` (pass the `WidgetDataResult` in the shape it expects — inspect `widget-renderer.tsx`/`widget-card.tsx` for the prop contract; reuse whatever renders a result). Show loading/empty/error states.
  - Footer: Back / Next / (on last step) Save + Cancel; plus a persistent **"متقدم"** button calling `onAdvanced()`.
  - `"use client"`; match Tailwind tokens (`h-9 border border-border bg-background px-2 text-sm`, `bg-primary text-primary-foreground`, `dir="rtl"`).

- [ ] **Step 2:** `npx next build` → green (0 type errors). Fix prop/shape mismatches against the real `widget-renderer`.
- [ ] **Step 3: Commit** `git add src/components/dashboard/business-widget-builder.tsx && git commit -m "feat(dashboards): business-concept widget builder (5-step, catalog-driven)"`

---

## Task 7: Wire in with Advanced toggle

**Files:** Modify `src/app/(dashboard)/dashboard/builder/page.tsx`, `src/app/(dashboard)/dashboard/page.tsx`.

- [ ] **Step 1: Builder page** — default to `<BusinessWidgetBuilder dashboardId={targetId} onSaved={() => router.push("/dashboard")} onCancel={() => router.push("/dashboard")} onAdvanced={() => setAdvanced(true)} />`. Add `const [advanced, setAdvanced] = useState(false)`; when `advanced`, render the existing `<WidgetBuilder onSave={...existing addWidget path...} onCancel={() => setAdvanced(false)} />`. Keep the existing Advanced save handler intact.

- [ ] **Step 2: Dashboard page inline modal** — same pattern: default `<BusinessWidgetBuilder dashboardId={activeId} onSaved={async () => { setBuilderOpen(false); await loadDetail(activeId); }} onCancel={() => setBuilderOpen(false)} onAdvanced={() => setAdvanced(true)} />`; `advanced` state swaps to the existing `<WidgetBuilder>` with its current onSave (`widgetPayloadFromSpec` → `addWidget` → reload). Do not remove the existing path.

- [ ] **Step 3:** `npx next build` → green.
- [ ] **Step 4: Commit** `git add "src/app/(dashboard)/dashboard/builder/page.tsx" "src/app/(dashboard)/dashboard/page.tsx" && git commit -m "feat(dashboards): default to business builder + Advanced toggle"`

---

## Task 8: Final FE build gate

- [ ] **Step 1:** `npx next build` → `✓ Compiled successfully`, 0 type errors, all pages built.
- [ ] **Step 2:** commit if any incidental fix.

---

## Self-Review

**Spec coverage:** 5-step business flow → Tasks 6,7. Widget Type/Business Data/Metric/Filters/Save → Task 6. Server-side metric→spec bridge (WidgetQuerySpec off the FE) → Tasks 1,2. Catalog consumption → Tasks 4,6. Advanced mode preserved → Task 7 (old builder untouched). Engine unchanged (reuse mapper/service/command) → Tasks 1,2. No FE tests (build-verified) → Tasks 4–8. Deferred exotic widget types / reference pickers → noted in Task 6.

**Placeholder scan:** The FE component (Task 6) is described by behavior + exact API calls + styling tokens rather than full literal TSX — necessary because it's a large interactive component with no test harness; every data call, output shape, and state is specified. Backend tasks carry full code. No `TBD`/vague-error steps.

**Type consistency:** `BuildSpec`/`WidgetTypeFor`/`PreviewAsync`/`CreateWidgetAsync` signatures consistent across Tasks 1↔2. `previewMetric`/`addWidgetFromMetric` request bodies match the controller request records (Tasks 2↔5). `CatalogQueryContext(_user.Permissions)` consistent. Visualization strings (`KpiCard/BarChart/LineChart/PieChart/DonutChart/Table`) consistent across `WidgetTypeFor` (T1), Step-0 cards (T6), and catalog `DefaultVisualization`.
