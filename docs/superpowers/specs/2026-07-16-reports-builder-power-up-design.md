# Reports Builder/Viewer Power-Up (SP-1a) — Design

**Date:** 2026-07-16
**Status:** Approved (design)
**Part of program:** Reports completion (SP-1a builder power-up → SP-1b organization/sharing → SP-2 scheduling infra → SP-3 dashboards backlog).

## Context / current state (verified 2026-07-16)

The Reports **backend is complete** for joins, formula fields, and runtime parameters after the parallel Phase-3 merge (`main@f304f2c`). What's missing is **builder/viewer UI** to use them, plus a permission gap. This sub-project is **frontend-only except one tiny backend permission edit**, and adds **no DB migration**.

Verified backend the UI will consume (canonical `src/lib/api/reports.ts`, already merged):
- **Relationships/joins:** `getReportRelationships(reportId)`, `addReportRelationship(reportId, { sourceObjectId, targetObjectId, joinField, joinType, sortOrder })`, `deleteReportRelationship(relationshipId)`. `JoinType = "Inner"|"Left"|"Right"` (a **string** on `AddReportRelationshipCommand`, no enum converter needed). Backend `ReportRelationshipRules` validates alias ordering (source must be the primary object or an already-introduced target at a strictly lower `sortOrder`).
- **Computed fields:** `addReportField(reportId, { fieldType: "CalculatedField", fieldCode, displayNameEn, displayNameAr, calculationText, formatPattern?, width?, sortOrder? })`; `validateFormula(formula) → { isValid, error? }` (pure server-side, safe to call as the user types). `ReportField.calculationText` column exists (migration `20260716150509_ReportFieldCalculationText`, applied). Enum props on report commands now bind string names (`main@f304f2c`).
- **Runtime parameters:** `ReportParameterBinder` — a filter with `isParameter=true` uses its stored `Value`/`ValueTo` as defaults, overridable per run. `runReport(id, { page, pageSize, parameters })` and `exportReport(reportId, format, fallbackName, parameters)` (serializes `p.<fieldCode>`; Between upper bound key is `<fieldCode>:to`, constant `ReportParameterBinder.UpperBoundSuffix = ":to"`). `addReportFilter(reportId, { fieldCode, operator, value?, valueTo?, isParameter? })`.
- **Object sourcing:** `getSelectableObjects()` returns `SelectableObject[] = { id /*ObjectDefinition Guid*/, code, nameAr, nameEn, module, catalog: CatalogObject }` where `catalog.fields: CatalogField[] = { code, nameEn, nameAr, fieldType, isMeasure, isGroupable, isFilterable, isDate, isReference, referenceObjectCode?, options? }`. It joins `GET /api/platform/objects` (registry Guids) with `GET /api/platform/registry/objects` (rich catalog).

Permission gap: `getSelectableObjects()` calls `GET /api/platform/objects` (`ObjectRegistryController`), still gated **`Platform.Objects.View`** only. The catalog endpoints were already widened to also accept `Platform.Reports.View`; the registry read endpoints were not. A report author with `Reports.Create/Edit` but not `Objects.View` gets an empty object list.

Design principle: reuse the canonical client and existing components (`ReportTable`, builder wizard, viewer). No migration.

Current builder file: `src/app/(dashboard)/reports/builder/[[...id]]/page.tsx` (5 steps: basics/fields/filters/grouping+sorting/preview). Viewer: `src/app/(dashboard)/reports/[id]/page.tsx`.

---

## Component 1 — Permission fix (backend, no migration)

**File:** `backend/src/HR.Modules/Platform/Controllers/ObjectRegistryController.cs`

Add `"Platform.Reports.View"` to the `[RequirePermission(...)]` on the two **read** endpoints only — `GetAll` (`GET`) and `GetByCode` (`GET {code}`) — so they read `[RequirePermission("Platform.Objects.View", "Platform.Reports.View")]` (the attribute is OR across the listed permissions, matching `ObjectCatalogController`). **Do NOT** widen the write endpoints (Create/Update/Delete/fields/relationships/permissions) — those stay `Platform.Objects.*`.

Verification: report author (Reports.View, no Objects.View) can `GET /api/platform/objects` (200), still cannot POST/PUT/DELETE object definitions (403).

---

## Component 2 — Builder Joins step (frontend)

**File:** `src/app/(dashboard)/reports/builder/[[...id]]/page.tsx` (add a step + panel)

Insert a new wizard step **"الروابط" (Joins)** immediately after Basics (so joined objects are available before choosing fields). Step index shifts: 0 basics, 1 joins, 2 fields, 3 filters, 4 grouping+sorting, 5 preview.

- Load `getReportRelationships(reportId)` for the current report; render the list (target object name · join field · join type) with delete (`deleteReportRelationship`).
- **Add relationship** form: `source` select (the primary object + every already-added relationship's target — the objects available as a join source, ordered by their introduction), `target` select (from `getSelectableObjects()`, excluding the primary + already-joined targets), `joinField` select (fields of the SOURCE object, from that object's `catalog.fields`), `joinType` select (`Inner`/`Left`/`Right`). `sortOrder` = current relationship count. Submit → `addReportRelationship`. On the backend's validation error (bad alias order / unknown field) the toast surfaces the message.
- Maintain an in-memory map of "objects in this report" = primary ∪ relationship targets, each with its registry `id` (Guid) and `catalog.fields`. The **Fields step** (Component 3) uses this so a user can add fields from any joined object.

## Component 3 — Fields step: joined-object fields + computed fields (frontend)

Same builder file, Fields step.

- **Object scope selector:** a small dropdown at the top of the available-fields list to choose which object in the report to pull fields from (primary or a joined target). Selecting a joined object lists ITS `catalog.fields`. Adding one sends `addReportField({ ..., objectDefinitionId: <that object's registry Guid> })` (primary-object fields send `objectDefinitionId: null` as today). Measure inference stays `field.isMeasure → fieldType "AggregateField"+aggregation "Sum"` else `"ObjectField"`.
- **Add computed field:** a toggle/section "＋ حقل محسوب" opens a mini-form: display name (Ar/En), a formula textarea, optional format pattern. As the user types the formula, debounced (~400ms) `validateFormula(formula)` shows a green "صيغة صحيحة" or the returned error text. Submit is disabled until `isValid`. On submit → `addReportField({ fieldType: "CalculatedField", fieldCode: <slug of display name, unique>, displayNameEn, displayNameAr, calculationText: formula, formatPattern?, width: 120, sortOrder: <count> })`. Computed fields appear in the selected-fields list with a "ƒ" marker and delete like any field.

## Component 4 — Filters step: parameter toggle (frontend)

Same builder file, Filters step.

- The filter-add form gains an **"معامل وقت التشغيل" (runtime parameter)** checkbox → passes `isParameter: true` to `addReportFilter`. The current-filters list shows a small "معامل" badge on parameterized filters. Everything else unchanged (field, operator string, value/valueTo).

## Component 5 — Viewer parameter prompt (frontend)

**File:** `src/app/(dashboard)/reports/[id]/page.tsx` (+ small component `src/components/reports/report-parameters.tsx`)

- After `getReport`, compute the parameterized filters (`report.filters.filter(f => f.isParameter)`). If any exist, render a **parameters panel** above the table: one labeled input per parameterized filter (label = the filter's field code / display); if the filter's `operator === "Between"`, render two inputs (value and `:to`). Inputs seed from the filter's stored `value`/`valueTo` defaults.
- A "تشغيل" (Run) button collects the inputs into a `ReportParameters` object (`{ [fieldCode]: value, [fieldCode + ":to"]: valueTo }`, omitting blanks) and calls `runReport(id, { page, pageSize: 50, parameters })`. The current run + paging use the same `parameters`.
- Export buttons pass the same `parameters` to `exportReport(id, format, code, parameters)` so the file matches the on-screen result. When there are no parameterized filters, the viewer behaves exactly as today (no panel).

---

## Testing & gates
- **Backend:** `dotnet build backend/src/HR.Api/HR.Api.csproj` = 0 errors. The permission change is a one-line attribute edit (behavior confirmed by the OR-permission pattern already used in `ObjectCatalogController`); no new unit test required (no logic added).
- **Frontend:** `npx next build` = 0 errors (no FE test runner). Manual verification against the live API for the builder end-to-end (join → fields incl. joined + computed → parameterized filter → preview) and the viewer parameter prompt.
- **Deploy:** backend zip-deploy once (perm change), then push → Vercel auto-deploys the FE. No migration.

## Known limits carried forward
- A report still cannot select two fields sharing a code across joined objects (engine throws — pre-existing R1 limit; the joins UI does not change that).
- Formula authoring surfaces `validateFormula` errors but does not autocomplete field names (out of scope here).
- Parameter inputs are plain text (no typed date/number pickers) — the backend binds strings; typed inputs are a later polish.

## Self-review
- No placeholders. Each component names its file, the exact client calls, and behavior.
- Consistent: all FE uses the canonical client; the one BE edit mirrors an existing pattern; no migration.
- Scope: single implementation plan (one perm edit + builder steps + viewer panel). Organization/sharing is deliberately SP-1b, not here.
- Ambiguity resolved: joined-object fields carry `objectDefinitionId`; computed fields use `fieldType:"CalculatedField"` + `calculationText`; parameters flow via `runReport`/`exportReport` `parameters` with the `:to` Between convention.
