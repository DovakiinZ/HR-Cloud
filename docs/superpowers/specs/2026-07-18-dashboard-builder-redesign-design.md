# Dashboard Builder Redesign — Design

**Date:** 2026-07-18
**Status:** Approved-by-standing-authorization (owner asked to finish sub-project #2 autonomously overnight and push to main). Design decisions below were made from the owner's original redesign brief + the Semantic Catalog foundation (#1); interactive approval wasn't possible.
**Sub-project:** #2 of the HubSpot-style UX redesign. Depends on #1 ([[semantic-catalog]]). Engine UNCHANGED.
**Related:** [[dashboard-platform-engine]], [[semantic-catalog]]

## Goal

Replace the technical widget builder flow (Object → Property → Aggregation → Visualization) with a business-concept-first flow an HR user understands:

1. **Widget Type** — KPI Card, Bar Chart, Line Chart, Pie/Donut, Table
2. **Business Data** — a Semantic Catalog *domain* (Employees, Payroll, Attendance, Leaves, Requests, Loans, Expenses, Documents)
3. **What to display** — a Semantic Catalog *metric* (Total Employees, Net Payroll, Late Employees, …)
4. **Filters (optional)** — Department, Branch, date range, etc.
5. **Save**

The existing technical builder is preserved verbatim as an **"Advanced"** mode (owner requirement: keep all advanced capability, just hide it until the user opts in).

## Constraints / non-goals

- **Engine unchanged.** No changes to `WidgetDataService`, `WidgetQuerySpec`, aggregation, or the object catalog. We reuse `MetricSpecMapper` (built in #1) + `IWidgetDataService.ExecuteAsync` + the existing `AddDashboardWidgetCommand`.
- **`WidgetQuerySpec` stays server-side for the business flow.** The new builder speaks only metric-code + friendly filters + visualization. Two thin backend endpoints materialize the metric → spec server-side (via `MetricSpecMapper`). The catalog contract stays purely semantic.
- **No new widget *types*.** Reuse existing renderers (KPI/Bar/Line/Pie/Donut/Table/Gauge). Calendar, Approval Queue, Timeline, Employee-List are deferred (need new renderers/data shapes) — a later sub-project.
- **Frontend is build-verified only** — the repo has no FE test framework (confirmed: no jest/vitest/testing-library). We do NOT add one (YAGNI). Backend gets TDD.
- No migration; no new dependency.

## Architecture

```
Business Widget Builder (new FE)
  Step1 Type → Step2 Domain → Step3 Metric → Step4 Filters → Step5 Save
        │ getDomains / getMetrics(domain)            │ previewMetric(metricCode, filters, viz)
        ▼ (Semantic Catalog API, #1)                 ▼ (new)
  /api/platform/catalog/*                    POST /api/platform/dashboards/widget-data/preview-metric  → WidgetDataResult
                                             POST /api/platform/dashboards/{id}/widgets/from-metric    → DashboardWidgetDto
                                                     │ server-side, reuse:
                                                     ▼
                                             ISemanticCatalogProvider.GetMetric → MetricSpecMapper.ToWidgetSpec
                                               → merge user filters → set visualization/limit → IWidgetDataService.ExecuteAsync
                                               → (create) AddDashboardWidgetCommand (existing)
```

The FE never constructs a `WidgetQuerySpec`. It holds `{ metricCode, filters: {field,value}[], visualization }` and renders the returned `WidgetDataResult` with the **existing** `widget-renderer`.

## Backend (new — all in HR.Modules.Platform, no engine change)

### `MetricWidgetService` (`Services/WidgetData/MetricWidgetService.cs`)
Interface `IMetricWidgetService`:
- `Task<WidgetQuerySpec> BuildSpecAsync(CatalogQueryContext ctx, string metricCode, IReadOnlyList<WidgetFilterSpec> userFilters, string? visualization, string? dateGranularity, DateTime nowUtc)`
  - `GetMetric(ctx, metricCode)`; if null → throw `NotFoundException` (metric missing/permission-denied → 404).
  - `MetricSpecMapper.ToWidgetSpec(metric.Definition, nowUtc)`.
  - Set `spec.Visualization = visualization ?? metric.DefaultVisualization`; `spec.DateGranularity = dateGranularity`; `spec.Limit ??= 12`; `spec.RequiredPermission = metric.RequiredPermissions.FirstOrDefault()`.
  - Append `userFilters` to `spec.Filters` (metric's baked filters first, user filters after).
- `Task<WidgetDataResult> PreviewAsync(...)` = `BuildSpecAsync` → `IWidgetDataService.ExecuteAsync(spec, null, ct)`.
- `Task<DashboardWidgetDto> CreateWidgetAsync(Guid dashboardId, ctx, metricCode, userFilters, visualization, dateGranularity, titleAr, titleEn, WidgetLayoutInput? layout, ct)` = `BuildSpecAsync` → serialize `Configuration = JSON({...spec, visualization})` → `Mediator.Send(new AddDashboardWidgetCommand { DashboardDefinitionId=dashboardId, WidgetType=WidgetTypeFor(visualization), TitleAr, TitleEn, Configuration, Layout })` → return dto.
- `WidgetTypeFor(visualization)`: pure map viz string → `WidgetType` enum (KpiCard/Gauge→KpiCard, Bar/HorizontalBar→BarChart, Line→LineChart, Pie→PieChart, Donut→DonutChart, Table/Leaderboard→Table, default KpiCard). Mirrors the FE `widgetTypeId`.

`BuildSpecAsync` and `WidgetTypeFor` are the unit-tested core (fake `ISemanticCatalogProvider`): metric→spec, viz default, user-filter append order, 404 on missing metric, viz→enum mapping.

### Controller endpoints
- `WidgetDataController`: `POST widget-data/preview-metric` `[RequirePermission("Platform.Dashboards.View")]` body `{ MetricCode, Filters: WidgetFilterSpec[], Visualization?, DateGranularity? }` → `ApiResponse<WidgetDataResult>` (builds `CatalogQueryContext` from `ICurrentUserService.Permissions`, `DateTime.UtcNow`).
- `DashboardsController`: `POST dashboards/{id:guid}/widgets/from-metric` `[RequirePermission("Platform.Dashboards.Edit")]` body `{ MetricCode, Filters, Visualization?, DateGranularity?, TitleAr, TitleEn, Layout? }` → `ApiResponse<DashboardWidgetDto>`.
- DI: register `IMetricWidgetService` → `MetricWidgetService` (scoped).

## Frontend (build-verified)

### `src/lib/api/catalog.ts` (new)
Types mirroring the catalog contract: `SemanticDomain`, `SemanticObject`, `SemanticField`, `SemanticMetric`, `SemanticMetricDefinition`, `SemanticFieldGroup`, `SemanticFilter`, `SemanticSort`. Functions (via `apiFetch`):
`getDomains()`, `getCatalogObjects(domain?)`, `getCatalogObject(code)`, `getMetrics(domain?)`, `getMetric(code)`, `searchCatalog(q)`.

### `src/lib/api/dashboards.ts` (extend)
- `previewMetric(metricCode, filters: WidgetFilterSpec[], visualization?, dateGranularity?): Promise<WidgetDataResult>` → `POST widget-data/preview-metric`.
- `addWidgetFromMetric(dashboardId, body: { metricCode; filters; visualization?; dateGranularity?; titleAr; titleEn; layout? }): Promise<DashboardWidget>` → `POST dashboards/{id}/widgets/from-metric`.

### `src/components/dashboard/business-widget-builder.tsx` (new)
The 5-step wizard. Props mirror the existing builder's `onCancel`, plus `onSaved: () => void` and `dashboardId` (it saves directly via `addWidgetFromMetric`, unlike the old builder which bubbled a spec). RTL Arabic, matches existing builder styling (`h-9 border border-border …`, terracotta/beige tokens).
- **Step 1 Widget Type:** card grid — KPI Card / Bar / Line / Pie / Donut / Table → sets `visualization` (`KpiCard|BarChart|LineChart|PieChart|DonutChart|Table`).
- **Step 2 Business Data:** `getDomains()` card grid (icon + nameAr) → `domain`.
- **Step 3 What to display:** `getMetrics(domain)` list (icon + nameAr + descriptionAr) → `metricCode`. Empty-state if the domain has no visible metrics.
- **Step 4 Filters (optional):** render the selected metric's `suggestedFilterFields`. For each, look up the field on the metric's object (via `getCatalogObject(objectCode)`): enum field → select of its options; date field → a from/to date-range → two filters (`gte`/`lte`); reference field → reuse the existing dashboard `filter-bar` reference-option loader if one exists, else a plain value input with the friendly field label. Produces `WidgetFilterSpec[]` (`{field, operator, value}`). Step is skippable.
- **Step 5 Save:** title (Ar; En defaults to Ar) → `addWidgetFromMetric(dashboardId, {...})` → `onSaved()`.
- **Live preview panel (right):** on any change with a chosen metric, call `previewMetric(metricCode, filters, visualization, granularity)` (debounced) and render the `WidgetDataResult` via the existing `widget-renderer`. Show a spinner/empty/error state.

### Wiring (`src/app/(dashboard)/dashboard/builder/page.tsx` + `dashboard/page.tsx`)
- Default to the **business builder**. A visible **"متقدم / Advanced"** toggle swaps to the existing `<WidgetBuilder>` (unchanged) for power users.
- Builder page: business builder saves via `addWidgetFromMetric(targetId, …)` then routes to `/dashboard`. Inline modal on dashboard page: saves then `loadDetail(activeId)`.
- The old `widget-builder.tsx` is untouched (Advanced mode).

## Testing

**Backend (TDD):**
- `MetricWidgetServiceTests` (fake `ISemanticCatalogProvider`, no DB): `BuildSpecAsync` maps a resolvable metric to a spec with the metric's baked filters; appends user filters after; sets `Visualization` from arg else `DefaultVisualization`; sets `DateGranularity`/`Limit`; throws `NotFoundException` when the metric is null (missing/permission). `WidgetTypeFor` maps each visualization string to the right `WidgetType`.
- Full solution build + `HR.Modules.Platform.Tests` green.

**Frontend:** `npx next build` green (type-check + compile). Manual/visual verification of the flow is the acceptance gate (documented, since there's no FE test runner).

## Rollout / deferred

- Deploy: backend has 2 new endpoints → API redeploy needed; FE via Vercel. (Owner asked to push to main; deploy follows the established recipe.)
- Deferred: reference-picker filters (Department/Branch by name) if the existing filter-bar has no reusable option loader; new widget types (Calendar/Approval Queue/Timeline/Employee List); default dashboards rebuilt on metrics (sub-project #4).
