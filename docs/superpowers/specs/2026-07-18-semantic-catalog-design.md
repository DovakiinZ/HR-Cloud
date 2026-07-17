# Semantic Catalog — Design

**Date:** 2026-07-18
**Status:** Approved (design), proceeding to plan
**Sub-project:** #1 of the HubSpot-style UX redesign program (foundation). Consumers #2–#6 (Dashboard Builder redesign, Reports Builder redesign, Default Dashboards, Default Reports, Localization polish) each get their own spec→plan cycle and depend on this.
**Related:** [[dashboard-platform-engine]], [[reports-engine-r1]]

## Problem & goal

The Dashboard and Report builders expose the raw object catalog: users pick a database Object → Property → Aggregation. HR users can't think that way, and business concepts like "Late Employees" or "Net Payroll" don't exist as pickable things — they must be hand-assembled. We need a **presentation/semantic layer** that describes everything the UI exposes in business terms, and makes curated business **metrics** first-class one-click concepts.

This is a **UX layer only**. The engine (Object Registry, `IObjectCatalogService`, `WidgetQuerySpec`, `IWidgetDataService`, Report Definitions, execution/aggregation) is correct and **must remain unchanged**. The Semantic Catalog *reads* the existing catalog to validate and enrich; it never modifies engine behavior.

The Semantic Catalog becomes the **single presentation layer for the whole platform** — every UI (builders, search, AI, future admin) talks only to its API, never to the raw registry.

## Non-goals / constraints

- No engine changes; no new execution path (metrics run through the existing `IWidgetDataService`).
- No DB migration; catalog is **code-defined** this phase.
- **Read-only** provider this phase. **No tenant overrides** yet — but the abstraction is preserved so a DB-backed / hybrid provider can replace the implementation later without changing consumers or the API contract.
- No app-wide i18n framework. Catalog carries `Ar`+`En`; Arabic-first UI reads `Ar`; a future English toggle just switches which field it reads.
- `WidgetQuerySpec` is **never** exposed in the public contract; it is an internal mapping target.

## Architecture

```
UI / AI / Search  ──HTTP──▶  SemanticCatalogController   (/api/platform/catalog/*)
                                        │
                                        ▼
                            ISemanticCatalogProvider      ← abstraction (HR.Application)
                                        │
                                        ▼
                            CodeDefinedSemanticCatalog     ← impl this phase (HR.Modules.Platform)
                                        │ reads / validates / enriches against
                                        ▼
                            IObjectCatalogService          ← existing engine catalog, UNCHANGED
```

- **Contract + interface** live in `HR.Application/SemanticCatalog/` so consumers depend on the abstraction, not the implementation.
- **Implementation** (`CodeDefinedSemanticCatalog`, the curated registry data, the metric→spec mapper) lives in `HR.Modules/Platform/Services/SemanticCatalog/`.
- Future storage swaps (DB / hybrid / external package) replace only the provider implementation. The API contract and consumers are unaffected.

Layering rule enforced: `UI → Semantic Catalog API → ISemanticCatalogProvider → implementation`. No consumer references the code registry directly.

## Public contract (DTOs — stable regardless of storage)

All in `HR.Application/SemanticCatalog/Contracts/`. All string codes are **stable and immutable** (renaming a code is a breaking change; add a new one instead). **No CLR/entity/column names ever appear in any returned field** except the internal `ObjectCode`/`FieldCode` identifiers, which are opaque stable tokens the UI passes back — never displayed.

```csharp
public sealed record SemanticDomain(
    string Code, string NameAr, string NameEn, string DescriptionAr, string DescriptionEn,
    string Icon, int SortOrder);

public sealed record SemanticObject(
    string ObjectCode,            // stable token → CatalogObject.Code (never displayed)
    string DomainCode,
    string NameAr, string NameEn, string DescriptionAr, string DescriptionEn,
    string Icon, IReadOnlyList<string> Keywords,
    bool DefaultVisible,
    IReadOnlyList<SemanticFieldGroup> FieldGroups,
    SemanticSort? DefaultSort,
    IReadOnlyList<SemanticFilter> DefaultFilters,
    IReadOnlyList<string> RecommendedMetricCodes,
    IReadOnlyList<string> RecommendedReportCodes,
    IReadOnlyList<string> RecommendedWidgetCodes,
    IReadOnlyList<SemanticField> Fields);

public sealed record SemanticFieldGroup(string Code, string NameAr, string NameEn, int SortOrder);

public sealed record SemanticField(
    string ObjectCode, string FieldCode,   // stable tokens (never displayed)
    string NameAr, string NameEn, string DescriptionAr, string DescriptionEn,
    string GroupCode, string? Icon, IReadOnlyList<string> Keywords,
    SemanticFieldRole Role,                // Dimension | Measure | Filter | Identifier
    bool DefaultVisible);

public enum SemanticFieldRole { Dimension, Measure, Filter, Identifier }

public sealed record SemanticMetric(
    string Code,                            // stable, immutable, e.g. "net_payroll"
    string NameAr, string NameEn, string DescriptionAr, string DescriptionEn,
    string Icon, string DomainCode,
    IReadOnlyList<string> RequiredPermissions,
    SemanticMetricDefinition Definition,
    string DefaultVisualization,            // "KpiCard" | "BarChart" | ...
    IReadOnlyList<string> SuggestedFilterFields);

// Abstract aggregation definition — NOT WidgetQuerySpec. Provider maps it internally.
public sealed record SemanticMetricDefinition(
    string ObjectCode,
    string Aggregation,                     // "Count"|"Sum"|"Average"|"Min"|"Max"|"DistinctCount"
    string? AggregationField,
    IReadOnlyList<SemanticMetricFilter> Filters,
    string? GroupByField);

public sealed record SemanticMetricFilter(
    string FieldCode, string Operator,      // "Equals"|"NotEquals"|"GreaterThan"|"LessThan"|"Between"|...
    string? Value,                          // literal value, OR
    string? RelativeValue,                  // relative-date token: "today" | "today+30d" | "startOfMonth" | "today-30d"
    string? ValueTo, string? RelativeValueTo); // upper bound for Between

public sealed record SemanticFilter(
    string FieldCode, string NameAr, string NameEn,
    string ControlType,                     // "select" | "date-range" | "search" | "reference"
    string? ReferenceObjectCode);           // for reference/select option source

public sealed record SemanticSort(string FieldCode, string Direction); // "Ascending"|"Descending"

// Diagnostics
public sealed record CatalogHealth(
    int VisibleObjects, int HiddenObjects, int VisibleMetrics, int HiddenMetrics,
    IReadOnlyList<HiddenItem> Hidden);
public sealed record HiddenItem(string Kind, string Code, string Reason); // Kind: Object|Field|Metric
```

## Provider interface

`HR.Application/SemanticCatalog/ISemanticCatalogProvider.cs`:

```csharp
public interface ISemanticCatalogProvider
{
    IReadOnlyList<SemanticDomain> GetDomains(CatalogQueryContext ctx);
    IReadOnlyList<SemanticObject> GetObjects(CatalogQueryContext ctx, string? domainCode = null);
    SemanticObject? GetObject(CatalogQueryContext ctx, string objectCode);
    IReadOnlyList<SemanticMetric> GetMetrics(CatalogQueryContext ctx, string? domainCode = null);
    SemanticMetric? GetMetric(CatalogQueryContext ctx, string metricCode);
    IReadOnlyList<SemanticSearchHit> Search(CatalogQueryContext ctx, string query);
    CatalogHealth GetHealth(); // ignores permissions; for admins/tests
}

public sealed record CatalogQueryContext(IReadOnlyCollection<string> Permissions);
public sealed record SemanticSearchHit(string Kind, string Code, string NameAr, string NameEn, double Score);
```

`CatalogQueryContext` carries the caller's effective permission strings (built in the controller from the existing claims/permission service). The provider is pure w.r.t. this context — easy to test.

## Provider behavior (CodeDefinedSemanticCatalog)

1. **Validation / self-adapting** — every object/field/metric is checked against `IObjectCatalogService`:
   - An object is hidden if its `ObjectCode` is not in the live catalog.
   - A field is hidden if its `FieldCode` isn't on the live object.
   - A metric is hidden if its `Definition.ObjectCode`/`AggregationField`/filter fields don't resolve.
   Hidden ≠ error: the item is omitted from consumer results, and recorded in the health report with a reason. (Mirrors `DashboardSeeder`'s skip-if-missing behavior — so `recruitment` and any not-yet-built entity simply don't surface.)
2. **Permission filtering** — **metrics** whose `RequiredPermissions` aren't all satisfied by `ctx.Permissions` are omitted from consumer endpoints (but still counted in health). Objects/fields have no per-item permission this phase — their visibility is governed by validation + `DefaultVisible`; the catalog controller is already gated by `Platform.Dashboards.View`/`Reports.View` at the API boundary.
3. **Observability** — at startup the provider logs a one-line summary (`N visible / M hidden objects, X visible / Y hidden metrics`) and logs each hidden item at `Debug`. The health endpoint exposes the full hidden list with reasons so missing mappings are never silently forgotten.
4. **Search** — ranks objects/fields/metrics by match against `Name*`, `Keywords`, and `Code`, in both Ar and En, using:
   - **Arabic normalization**: strip tashkeel (diacritics), unify alef forms (أ إ آ → ا), taa marbuta (ة → ه), alef maqsura (ى → ي), remove tatweel (ـ). A pure helper `ArabicText.Normalize`.
   - **Synonyms**: a curated Ar/En synonym map (e.g. راتب/payroll/salary, موظف/employee/staff, غياب/absent, تأخير/late, إجازة/leave/vacation) expands the query before matching.
   Results are permission- and validation-filtered.

## Metric definition → WidgetQuerySpec (internal mapper, not exposed)

`MetricSpecMapper` (in `HR.Modules.Platform`, internal to the impl) converts a validated `SemanticMetricDefinition` into the existing `WidgetQuerySpec`:
- `ObjectCode/Aggregation/AggregationField/GroupByField` pass through 1:1.
- Each `SemanticMetricFilter` → `WidgetFilterSpec`. `RelativeValue` tokens are resolved to literal dates via a pure `RelativeDate.Resolve(token, nowUtc)` helper (`today`, `today±Nd`, `startOfMonth`, `endOfMonth`). `Between` uses `Value/RelativeValue` + `ValueTo/RelativeValueTo`.
- The resulting spec is exactly what today's engine executes — proving metrics are one-click executable with zero engine change. (Sub-project #2/#4 use this mapper to materialize widgets; this sub-project ships the mapper + tests, not the builder UI.)

The mapper is the ONLY place that knows about `WidgetQuerySpec`. It never appears in the API contract.

## API surface

New `SemanticCatalogController` at `/api/platform/catalog`, read-only, permission-gated `[RequirePermission(Platform.Dashboards.View, Platform.Reports.View)]` (any-of, mirroring `ObjectCatalogController`). The controller builds `CatalogQueryContext` from the caller's permissions.

| Method | Route | Returns |
|---|---|---|
| GET | `/domains` | `SemanticDomain[]` |
| GET | `/objects?domain={code}` | `SemanticObject[]` |
| GET | `/objects/{objectCode}` | `SemanticObject` (404 if hidden/missing) |
| GET | `/metrics?domain={code}` | `SemanticMetric[]` |
| GET | `/metrics/{metricCode}` | `SemanticMetric` (404 if hidden/missing) |
| GET | `/search?q={query}` | `SemanticSearchHit[]` |
| GET | `/health` | `CatalogHealth` (gated to an admin permission, e.g. `Platform.Dashboards.Manage`; also used by tests) |

## Initial curated content

**Domains (9):** `employees`, `payroll`, `attendance`, `leaves`, `requests`, `loans`, `expenses`, `documents` (live), plus `recruitment` (defined, self-hidden until entities exist).

**Objects:** friendly `SemanticObject` for the primary entity of each live domain (Employee, PayrollPayslip, AttendanceRecord, LeaveBalance, RequestInstance, Loan, Expense/EmployeeExpense, GeneratedDocument), each with curated Ar/En name, description, icon, keywords, field groups, default sort, default filters, and recommended-metric codes. Exact `ObjectCode`/`FieldCode` bindings are verified against the live `IObjectCatalogService` during implementation (Task 1 inventories them); anything absent self-hides.

**Field groups (business-friendly):** `personal_information`, `employment`, `organization`, `payroll`, `attendance`, `leave`, `documents`. Each object assigns its fields to one of these.

**Metrics (17, prioritized for HR comprehension):**

| Code | Ar name | Object | Aggregation | Notes |
|---|---|---|---|---|
| `total_employees` | إجمالي الموظفين | Employee | Count | |
| `active_employees` | الموظفون النشطون | Employee | Count | filter Status = Active |
| `new_employees` | الموظفون الجدد | Employee | Count | HireDate ≥ startOfMonth (relative) |
| `employees_by_department` | الموظفون حسب الإدارة | Employee | Count | groupBy DepartmentId; default BarChart |
| `gross_payroll` | إجمالي الرواتب | PayrollPayslip | Sum | GrossSalary field |
| `net_payroll` | صافي الرواتب | PayrollPayslip | Sum | NetSalary field |
| `total_gosi` | إجمالي التأمينات | PayrollPayslip | Sum | GOSI field |
| `total_additions` | إجمالي الإضافات | PayrollPayslip | Sum | additions field |
| `total_deductions` | إجمالي الخصومات | PayrollPayslip | Sum | deductions field |
| `late_employees` | الموظفون المتأخرون | AttendanceRecord | Count | Status = Late |
| `absent_employees` | الموظفون الغائبون | AttendanceRecord | Count | Status = Absent |
| `overtime_hours` | ساعات العمل الإضافي | AttendanceRecord | Sum | overtime field |
| `remaining_leave_balance` | رصيد الإجازات المتبقي | LeaveBalance | Sum | remaining field |
| `pending_requests` | الطلبات المعلقة | RequestInstance | Count | Status = Pending |
| `pending_approvals` | الموافقات المعلقة | RequestInstance | Count | approval-pending filter |
| `expiring_documents` | المستندات المنتهية | GeneratedDocument | Count | ExpiryDate Between today..today+30d |
| `expiring_contracts` | العقود المنتهية | Employee | Count | ContractEndDate Between today..today+30d |

Each metric carries `RequiredPermissions` (e.g. payroll metrics → `Platform.Payroll.View`-class), `DefaultVisualization`, and `SuggestedFilterFields` (Department, Branch, JobTitle, Nationality, EmploymentType, DateRange as applicable). Exact field codes and permission strings are bound in implementation against the live catalog/permission set; metrics that don't resolve self-hide and appear in health.

## Localization

Every domain/object/field/metric carries `NameAr`+`NameEn`+`DescriptionAr`+`DescriptionEn`. Arabic uses proper business terminology (no transliterated technical terms). The API returns both; the Arabic-first UI reads `Ar`. English-UI toggle and full i18n are deferred (sub-project #6 / future).

## Testing (TDD)

Pure, DB-free unit tests over the registry + provider (using a fake `IObjectCatalogService` seeded with representative objects/fields):

1. **Registry integrity:** no duplicate domain/object/field/metric codes; every object's `DomainCode` is a defined domain; every field's `GroupCode` is a defined group on its object; every metric's `DomainCode` defined; every `RequiredPermissions` non-empty; every metric's `DefaultVisualization` is a known viz.
2. **Metric resolvability:** with a catalog containing the expected objects/fields, every metric's `Definition` maps (via `MetricSpecMapper`) to a valid `WidgetQuerySpec` (object exists, aggregation valid, aggregationField present when required, filter fields exist).
3. **Self-hiding:** an object/field/metric whose backing object/field is absent from the fake catalog is omitted from consumer results AND present in `GetHealth().Hidden` with a reason.
4. **Permission filtering:** a metric requiring `Platform.Payroll.View` is omitted for a context without it, included with it; health still counts it.
5. **Arabic normalization:** `ArabicText.Normalize` unifies alef/taa-marbuta/tashkeel/tatweel (table-driven cases).
6. **Search:** query "راتب" matches the payroll metrics; "late" and "تأخير" both match `late_employees` (synonym + normalization); results are permission/validation filtered.
7. **RelativeDate.Resolve:** `today`, `today+30d`, `today-7d`, `startOfMonth`, `endOfMonth` resolve correctly against a fixed clock.

Provider/mapper are pure (inject `IObjectCatalogService` + a clock) → no DB. A thin controller wiring test is optional.

## File structure

**Create**
- `HR.Application/SemanticCatalog/Contracts/*.cs` — the DTOs + enum above.
- `HR.Application/SemanticCatalog/ISemanticCatalogProvider.cs` + `CatalogQueryContext`.
- `HR.Modules/Platform/Services/SemanticCatalog/CodeDefinedSemanticCatalog.cs` — provider impl.
- `HR.Modules/Platform/Services/SemanticCatalog/CatalogRegistry.cs` — the curated data (domains, objects, fields, metrics, synonyms, field-groups). May be split into partials (Domains/Objects/Metrics) for readability.
- `HR.Modules/Platform/Services/SemanticCatalog/MetricSpecMapper.cs` — Definition→WidgetQuerySpec (internal).
- `HR.Modules/Platform/Services/SemanticCatalog/ArabicText.cs` + `RelativeDate.cs` — pure helpers.
- `HR.Modules/Platform/Controllers/SemanticCatalogController.cs`.
- Tests under `HR.Modules.Platform.Tests/SemanticCatalog/`.

**Modify**
- `HR.Modules/Platform/DependencyInjection.cs` — register `ISemanticCatalogProvider` → `CodeDefinedSemanticCatalog` as **scoped** (it depends on the scoped `IObjectCatalogService`; scoped avoids a captive dependency). The curated registry data itself is static/immutable, so there's no per-request cost beyond validation against the already-cached object catalog.

## Deferred (later sub-projects / future)

- Dashboard/Report builder UIs that consume this API (#2, #3).
- Default dashboards/reports built from metrics (#4, #5); the `RecommendedReportCodes`/`RecommendedWidgetCodes` fields are populated but their targets are seeded later.
- Localization polish / English toggle (#6).
- DB-backed / hybrid provider + tenant overrides + admin editing UI (future; the abstraction is preserved for it).
