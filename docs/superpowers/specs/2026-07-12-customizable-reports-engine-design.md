# Customizable Reports Engine — Design (R1)

**Date:** 2026-07-12
**Status:** Approved (design), ready for implementation planning
**Scope decision:** Reports Engine only. The Dashboard Platform engine already exists and is reused, not rebuilt.

---

## 1. Context & Problem

The platform already ships a **Dashboard Platform engine** (object catalog + dynamic-SQL aggregation + widget builder/grid/drilldown) and a **Reports definition layer** (persisted CRUD for report definitions, fields, filters, groupings, sortings, schedules, templates at `/api/platform/reports`). What is **missing** is the part that makes reports actually usable:

- No **execution engine** — nothing turns a `ReportDefinition` into rows with dynamic sorting, multi-level grouping, and aggregates.
- No **export** wiring for reports (Excel/CSV writers exist as `IExportWriter`; PDF/SIF do not).
- No **frontend** — the reports page is a "coming soon" placeholder.
- No **computed/formula fields**, **sharing management**, or **organization** (favorites/folders/tags).

This spec covers **R1**: a metadata-driven execution engine (with joins and computed fields from day one), ownership/sharing, organization, a builder + viewer UI, and Excel/CSV export. Later increments: **R2** PDF + streaming, **R3** SIF/WPS export, **R4** scheduled delivery.

### Non-negotiable principle
**No hardcoded report logic.** Every object, field, join, filter, computed formula, sort, and group is defined as metadata and resolved at runtime. New reports never require code changes.

---

## 2. Reused Infrastructure (do NOT duplicate)

| Concern | Existing asset | Reuse |
|---|---|---|
| Object/field/table metadata | `ObjectRegistry` (`ObjectDefinition.TableName`, `ObjectField`, `ObjectRelationship.ForeignKeyField`) | Source of truth for what a report can query and how objects join. |
| Identifier safety whitelist | `IObjectCatalogService` (runtime EF-model introspection, code-keyed) | Every table/column/join **validated against the live model** before SQL composition. Injection gate. |
| Definition CRUD | `ReportsController` + `ReportCommands`/`ReportQueries`/`ReportDtos` | Kept as-is; execution/sharing/organization endpoints added alongside. |
| Formula evaluation | `ExpressionEvaluator` (pure AST → `RuleValue`, `FunctionRegistry`, AST-as-JSON via `AstJson`) | Computed fields compile to an `Expr` AST and evaluate per row. Extend `FunctionRegistry` with report helpers. |
| Tabular export | `IExportWriter` (format-pluggable: Excel/ClosedXML, CSV, Txt, Xml) + `TabularDataset` | Report results serialize through this. **Reuse the `IExportWriter` name — do not introduce a parallel `IExportProvider`.** |
| Sharing targets | `ReportShare` (`SharedWithUserId` / `SharedWithRoleId` / `SharedWithDepartmentId` + `CanEdit`) | Backs Roles / Specific Users / Departments sharing. No new sharing table. |
| Design system | Thamania Editorial tokens in `globals.css` (zero-radius, terracotta `#C25A3F`, beige `#FDFBF7`, Thmanyah fonts) | UI uses existing tokens/components. No new design-system work. |
| Scheduling pattern (R4) | `IPayrollExecutionScheduler` (InProcess + Hangfire impls), `IBackgroundExecutionContext` | Mirror for `IReportExecutionScheduler`. |
| Delivery (R4) | `EmailNotificationQueue` + `NotificationService` | Enqueue exported reports to recipients. |

---

## 3. Data Model

### 3.1 Reused as-is (no schema change)
`ReportDefinition`, `ReportField` (incl. `FieldType = CalculatedField | RelationshipField | AggregateField | ObjectField`, `CalculationExpression`, `Aggregation`, `FormatPattern`), `ReportFilter`, `ReportGrouping`, `ReportSorting`, `ReportRelationship` (`SourceObjectId`, `TargetObjectId`, `JoinField`, `JoinType` Inner/Left/Right), `ReportShare`, and the `ObjectRegistry` trio.

### 3.2 New (single migration: `ReportOrganization`)
- **`ReportFolder`** : `TenantEntity` — `NameEn`, `NameAr`, `ParentFolderId?` (nestable). `ReportDefinition` gains `FolderId?`.
- **`ReportTag`** : `TenantEntity` — `Name`, `Color?`. Join table **`ReportDefinitionTag`** (`ReportDefinitionId`, `ReportTagId`).
- **`ReportUserState`** : per-user state — `UserId`, `ReportDefinitionId`, `IsFavorite`, `IsPinned`, `LastViewedAt`. One row per (user, report). Powers Favorites, Pinned, and Recent.

`CalculationExpression` stores the **compiled `Expr` AST as JSON** (via `AstJson`), not raw text — the raw formula string may be kept alongside for round-trip editing in the field metadata.

---

## 4. Execution Engine (core)

New `ReportExecutionService` in `HR.Modules/Platform/Services/Reports/`, beside `WidgetDataService` (which it does **not** replace — that one is single-value widget aggregation; this is multi-column tabular + hierarchical grouping).

### 4.1 Pipeline
```
1. Load definition: fields, filters, groupings, sortings, relationships, primary object.
2. Resolve Guids → ObjectDefinition (TableName/Code) for primary + each relationship.
   VALIDATE every table, column, and FK against IObjectCatalogService.   ← injection gate
   Reject anything not present in the live model.
3. SQL tier — build ONE parameterized query:
     SELECT (object fields + relationship fields)
     FROM primary  JOIN related ON <validated FK>  (Inner/Left/Right per ReportRelationship)
     WHERE  (filters on object/relationship fields, values bound as parameters)
            AND tenant scope AND soft-delete scope   (applied automatically)
     ORDER BY (sortings on object/relationship fields)
4. Materialize rows. R1: configurable safety cap (max rows) with a surfaced warning when hit.
   R2 lifts this via streaming.
5. In-memory tier — evaluate CalculatedFields per row via ExpressionEvaluator
   (row projected into IEvaluationContext; variables = object/joined field values).
6. Apply filters/sorts/groups that reference computed fields (in-memory).
7. Multi-level grouping → hierarchical result:
     groups (nested by ReportGrouping order) → rows,
     per-group aggregates (Sum/Avg/Count/Min/Max), subtotals, grand total.
8. Return ReportResult { columns[], groups[]/rows[], totals, page, truncated? }.
```

### 4.2 Two-tier rationale
Pure columns and joins push down to SQL (fast, paginated, DB-side sort/filter). Computed fields are C#/AST and cannot generally translate to SQL, so they — and any filter/sort/group that references them — resolve **in-memory after materialization**. Summary/Matrix reports materialize fully regardless (subtotals need the whole set). The R1 row-cap bounds worst-case memory; R2 streaming removes it for export paths.

### 4.3 Safety
- No untrusted identifier is ever string-interpolated. Table/column/FK names are accepted **only** if they resolve in `IObjectCatalogService`.
- All filter values are bound parameters.
- Tenant + soft-delete predicates are always injected, matching `WidgetDataService`.
- Field-level access: a report cannot expose an object the caller lacks permission to read (reuse `ObjectPermission` where applicable).

---

## 5. Computed / Formula Fields

Metadata-driven, zero per-report code.

- A `ReportField` with `FieldType = CalculatedField` carries a formula compiled to an `Expr` AST (JSON) in `CalculationExpression`.
- Evaluated by the existing `ExpressionEvaluator`; variables bind to the report's other (object/joined) fields on each row.
- `FunctionRegistry` is extended with report helpers: `age(date)`, `yearsBetween(a,b)`, `now()`, `today()`, `concat(...)`, `coalesce(...)`, `round(n,d)`, plus the existing arithmetic/logical operators.
- Computed fields are first-class: they can be **displayed, filtered, sorted, grouped, and exported** (filter/sort/group on them happen in the in-memory tier).

Worked examples (all pure metadata):
- **Full Name** = `concat(firstName, ' ', lastName)`
- **Age** = `age(dateOfBirth)`
- **Years of Service** = `yearsBetween(hireDate, now())`
- **Salary After GOSI** = `basicSalary - basicSalary * gosiRate`

Service-derived values (e.g., **Remaining Leave**) are **not** special-cased in the engine. They are exposed as **queryable fields on their object or a joined object** in the `ObjectRegistry`; the formula then references them like any other field. This preserves the no-hardcoded-logic rule. Any such field's data source is confirmed during planning of the relevant field.

---

## 6. Ownership, Sharing & Organization

### 6.1 Ownership & visibility
- `ReportDefinition.OwnerId` + `Scope`:
  - **Private** = `Personal` (owner only)
  - **Public** = `Company` (whole tenant)
- **Roles / Specific Users / Departments** = `ReportShare` rows (`SharedWithRoleId` / `SharedWithUserId` / `SharedWithDepartmentId`, `CanEdit`).
- A central **access resolver** gates every list/get/run/edit:
  - **Read** if: owner ∨ `Scope=Company` ∨ a matching `ReportShare` (user/role/department).
  - **Edit** if: owner ∨ share with `CanEdit`.
- New **share-management endpoints** (the existing controller has none): add/remove/list shares.

### 6.2 Organization
- **Folders**: nestable `ReportFolder` CRUD; report assigned via `FolderId`.
- **Tags**: `ReportTag` CRUD + assign/unassign to reports.
- **Favorites / Pinned**: `POST {id}/favorite`, `POST {id}/pin` toggles → `ReportUserState`.
- **Recent**: top-N by `ReportUserState.LastViewedAt`, stamped on each open/run.
- Listing supports `GET /reports?view=favorites|recent|pinned&folderId=&tagId=`.

---

## 7. API Surface (added to `/api/platform/reports`)

| Method | Route | Purpose |
|---|---|---|
| POST | `{id}/run` | Execute report → paged `ReportResult`; accepts parameter values for parameterized filters. Stamps `LastViewedAt`. |
| GET | `{id}/export?format=excel\|csv` | Export current definition result (R1 formats). PDF/SIF added in R2/R3. |
| POST/DELETE/GET | `{id}/shares` | Manage `ReportShare` rows. |
| GET/POST/PUT/DELETE | `folders` | Folder CRUD (nestable). |
| GET/POST/DELETE | `tags`, `{id}/tags` | Tag CRUD + assignment. |
| POST | `{id}/favorite`, `{id}/pin` | Toggle per-user state. |

All gated by `Platform.Reports.*` permissions plus the access resolver.

---

## 8. Frontend (`src/app/(dashboard)/reports/`)

Uses existing Thamania tokens/components; no new design system.

- **List page**: folders sidebar (nested), view filters (All / Favorites / Recent / Pinned), tag filter, search. Cards show name, owner, scope, tags; pin/favorite actions.
- **Builder** (linear vertical wizard, matching the platform's wizard UX): pick primary object → add joins (suggested from `ObjectRelationship`) → choose fields (object / relationship / **computed formula input** / aggregate) → filters → grouping & sorting → save + share. Wired onto the existing definition CRUD endpoints.
- **Viewer**: grouped table with subtotals + grand total, respects field format patterns, parameter inputs for parameterized filters, RTL, **Excel/CSV export buttons**.

---

## 9. Testing (R1 is test-first, xUnit — matches Finance module)

Pure, high-value units get failing-test-first coverage:
- SQL/join builder: correct SELECT/JOIN/WHERE/ORDER BY, parameterization, tenant + soft-delete scoping, rejection of non-whitelisted identifiers.
- Computed-field evaluation: each helper function + formula-over-row cases.
- Grouping & aggregates: multi-level grouping, Sum/Avg/Count/Min/Max, subtotals, grand total.
- Access resolver: owner / company / user-share / role-share / department-share / edit-vs-read matrix.
- Organization: favorites/pinned/recent state transitions.

Integration: end-to-end `POST {id}/run` against a seeded multi-object report (Employee + Department join, a computed field, a group + aggregate).

---

## 10. Increment Boundaries (explicit)

**In R1:** joins, object/relationship/computed/aggregate fields, filter/sort/multi-level grouping, ownership/sharing, organization, builder + viewer UI, Excel/CSV export, row-capped materialization.

**Deferred:**
- **R2** — server-side PDF (QuestPDF `IExportWriter`, new `Pdf` format) + stream-based generation (removes R1 row cap on export path).
- **R3** — generic report→SIF/WPS `IExportWriter`.
- **R4** — `IReportExecutionScheduler` (InProcess/Hangfire) running due `ReportSchedule`s → export → `EmailNotificationQueue` delivery.

---

## 11. Open items to confirm at planning time
- Exact `FunctionRegistry` helper set and formula-string → AST compiler (reuse or thin parser over existing AST types).
- Whether "Remaining Leave"-class values already exist as queryable registry fields or need to be exposed first.
- Default row-cap value and how truncation is surfaced in the viewer.
