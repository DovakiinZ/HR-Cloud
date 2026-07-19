# Reports Engine — Builder/Viewer UI + Scheduling Runner + WPS/SIF Export (Design)

**Date:** 2026-07-16
**Status:** Approved (design)
**Supersedes/continues:** Reports Engine R1 (Phase 1 execution, Phase 2 access/sharing/org, Phase 3a export — all shipped). This closes the three remaining gaps: **3b builder/viewer UI**, **R4 scheduling**, **R3 SIF/WPS export**.

## Context / current state (verified 2026-07-16)

The reports **backend definition surface is complete**. `ReportsController` (`api/platform/reports`) already exposes: report CRUD, `run` (paged), `export` (excel/csv/pdf), `publish`, `clone`, and granular add/delete for **fields, filters, groupings, sortings, schedules, shares, folders, tags**, plus favorites/pin. The object catalog (`api/platform/registry/objects[/{code}/fields]`) is live. The only shipped UI is the reports **list + export buttons** (`src/app/(dashboard)/reports/page.tsx`).

Key reuse discoveries:
- **`ReportSchedule` entity already has `LastRunAt`/`NextRunAt`** and its table exists (CRUD endpoints live) → **R4 needs no migration**.
- **Saudi WPS SIF already implemented** in `HR.Application/Engines/Finance/Export/Bank/` (`SaudiWpsSifProfile`, `SaudiWpsSifValidator`, `BankPipeline`, `IBankProfile`, `BankField(Key,Label,Source,Format?)`) → R3 maps a report through it, no new format engine.
- **`DocumentExpiryHostedService : BackgroundService`** + `IDocumentExpiryScanner` is the proven in-process background pattern to mirror for R4. Background jobs get tenant scope via `IBackgroundExecutionContext`.
- **`EmailNotificationQueue`** (`ToEmail/Subject/Body/Link/...`) has **no attachment field** → scheduled delivery stores the export via the existing file store and emails a **download link**.
- Export plumbing: `IReportExportService.ExportAsync(reportId, ExportFormat, ct)` → `RunForExportAsync` → `ReportResultFlattener.Flatten` → `IExportWriter` by `ExportFormat`; `ExportReportQuery(Guid Id, string Format)` parses the format string.

Design principle: **reuse, do not fork.** No DB migration in any of the three sub-projects.

---

## Sub-project A — Builder + Viewer UI (frontend only)

Pure Next.js 16 (App Router, `(dashboard)` group, RTL, Thamania editorial tokens). No backend change. Wires the existing granular endpoints.

### A1. API client (`src/lib/api/reports.ts`, extend)
Add typed calls: `getReport(id)`, `createReport(body)`, `updateReport(id, body)`, `deleteReport(id)`, `publishReport(id)`, `runReport(id, {page,pageSize})`; field/filter/grouping/sorting `add*`/`delete*`; and catalog fetch `getCatalogObjects()` / `getObjectFields(code)` (reuse `apiFetch`). Add DTO interfaces mirroring `ReportDefinitionDto`, `ReportFieldDto`, `ReportFilterDto`, `ReportGroupingDto`, `ReportSortingDto`, `ReportResult` (columns/groups/rows/grandTotals/truncated/totalCount/page/pageSize), and catalog object/field shapes.

### A2. Builder wizard `src/app/(dashboard)/reports/builder/[[...id]]/page.tsx`
Optional catch-all: `/reports/builder` = create, `/reports/builder/{id}` = edit. Five steps:
1. **Basics** — primary object (from catalog), code, nameAr/nameEn, description, `reportType`, `scope`. On "next" from step 1 in create mode → `createReport` and switch to edit mode (so subsequent granular calls have an id). Edit mode → `updateReport`.
2. **Fields** — pick object fields from the catalog (checkbox list, with measure/aggregate options and optional computed-expression field); each add → `addField`, remove → `deleteField`. Show current field list with reorder-by-sortOrder.
3. **Filters** — field + operator + value(s); `addFilter`/`deleteFilter`.
4. **Grouping + Sorting** — pick grouping fields (`addGrouping`) and sort fields+direction (`addSorting`).
5. **Preview** — `runReport(id,{page:1,pageSize:50})` rendered via the shared viewer table (below); buttons: **Publish**, **Save & close**, **Open full viewer**.

Wizard state is server-backed (each step persists), so navigation is resilient. Validation: required basics before leaving step 1; at least one field before preview.

### A3. Viewer `src/app/(dashboard)/reports/[id]/page.tsx`
Runs the report and renders results. Shared table component `src/components/reports/report-table.tsx`:
- Flat result → paged table (page controls using `totalCount/page/pageSize`).
- Grouped result → nested group headers, per-group **subtotal** rows, and a **grand total** row (consumes `ReportResult.groups[].aggregates` + `grandTotals`).
- **`Truncated` banner** when true ("Showing first N rows; refine filters to see all").
- Export buttons (excel/csv/pdf + WPS/SIF from Sub-project C) reusing `exportReport`.
- Header actions: **Edit** (→ builder), favorite/pin toggles.

### A4. List page actions (`reports/page.tsx`, extend)
Add **New report** (→ builder, gated `Platform.Reports.Create`), row **Open** (→ viewer), **Edit** (→ builder, gated `Platform.Reports.Edit`). Keep existing export column.

**Testing (A):** `next build` green; component-level guards mirror existing `usePermission` pattern. (Repo has no FE test runner; verification is build + manual against live API.)

---

## Sub-project B — Scheduling runner + delivery (backend + small UI)

No migration (schedule table + `LastRunAt`/`NextRunAt` exist).

### B1. `IReportScheduleRunner` + `ReportScheduleRunner` (`HR.Modules.Platform/Services/Reports/`)
`Task<int> RunDueAsync(CancellationToken ct)` — returns number of schedules processed. Logic:
- Load `ReportSchedule` rows where `IsActive && (NextRunAt == null || NextRunAt <= DateTime.UtcNow)`, across tenants.
- For each: set tenant context (via `IBackgroundExecutionContext`), then `IReportExportService.ExportAsync(reportId, schedule.ExportFormat, ct)` → `ReportExportFile`.
- **Store** the file bytes via the existing file store (same store employee-export/documents use), producing a retrievable id/url.
- Parse `Recipients` (JSON list of emails/userIds) → enqueue one `EmailNotificationQueue` row each: subject `"Scheduled report: {NameEn}"`, body with the run time, `Link` = download url, `Category = "ReportSchedule"`, `EntityId = reportId`.
- Stamp `LastRunAt = now`; compute `NextRunAt` from `Frequency`:
  - `Daily` → +1 day, `Weekly` → +7 days, `Monthly` → +1 month (from now, normalized to the same wall-clock). `CronExpression` is **not** parsed in this increment (documented limit; frequency drives cadence).
- Per-schedule try/catch so one failure doesn't halt the batch; log failures.

### B2. `ReportScheduleHostedService : BackgroundService` (`HR.Api/Services/`)
Mirror `DocumentExpiryHostedService`: initial 1-minute delay, then tick **hourly**; each tick resolves `IReportScheduleRunner` in a scope and calls `RunDueAsync`. Register in `Program.cs` (`AddHostedService`). Runner + interface registered in Platform DI.

### B3. Schedule UI
In the builder step 5 / viewer header, a **Schedules** panel: list existing schedules (needs a `GetReportSchedulesQuery` + `GET {id}/schedules` — the delete/add endpoints exist; add the list query/endpoint), add (frequency + export format + recipients), delete. FE calls in `reports.ts`.

**Testing (B):** DB-free unit tests for `NextRunAt` computation per frequency and for recipient parsing; `[SkippableFact]` (gated `REPORTS_TEST_DB`) end-to-end for `RunDueAsync` producing a stored file + queued email row. Runner must be pure-enough to test the due-selection + scheduling math without the hosted service.

---

## Sub-project C — WPS/SIF report export (backend + button)

Reuse the existing bank pipeline; no new format engine, no migration.

### C1. `SifReportExporter` (`HR.Modules.Platform/Services/Reports/`)
`byte[] Export(ReportResult result)` (or operate on the flattened `TabularDataset`): map report columns to the `SaudiWpsSifProfile` field set by **well-known column codes** (`EmployeeNumber, NationalId, EmployeeName, Iban, BankCode, NetAmount, Currency`). Feed rows through `BankPipeline` + `SaudiWpsSifProfile` to produce the SIF (CSV per the profile), run `SaudiWpsSifValidator`. **Missing required columns → `ValidationException` (400)** naming the missing codes.

### C2. Wire into export path
`ExportReportQuery` already takes a format string. Add `"sif"` handling: when `format == "sif"`, route to `SifReportExporter` instead of the generic `IExportWriter` lookup (SIF is a profile-driven mapping, not a plain `ExportFormat`). Return `ReportExportFile(bytes, "text/csv", "{code}-wps-sif-{yyyyMMdd}.csv")`. Keep gated by `Platform.Reports.Export`.

### C3. UI button
Add a **WPS/SIF** button to the list/viewer export group. On a report lacking the columns, the 400 surfaces a clear toast ("Report is missing WPS columns: …").

**Testing (C):** DB-free unit tests — a `ReportResult` with the WPS columns produces a valid SIF (validator passes, header/rows present); a `ReportResult` missing `Iban` throws `ValidationException` naming `Iban`.

---

## Build order, deploy, and constraints

1. **A** (frontend) → 2. **B** (backend runner + hosted service + schedule UI) → 3. **C** (backend SIF + button).
- Each sub-project: TDD (`test-driven-development`) + subagent-driven-development, commit per task (`feat(reports):` / `test(reports):` / `fix(reports):`).
- **No DB migration** in any sub-project.
- Deploy backend **once** after B + C: `dotnet publish HR.Api -c Release`, zip with **forward-slash** entries (Python zipfile, not `Compress-Archive`), `az webapp deploy --resource-group HR --name hrcloud-api-v4xd --type zip`. Frontend auto-deploys to Vercel on push to `main`.
- Backend build gate: `dotnet build backend/src/HR.Api/HR.Api.csproj` = 0 errors; `dotnet test backend/tests/HR.Modules.Platform.Tests` green (DB-touching skipped locally). Frontend: `next build` green.

## Known limits (documented, carried forward)
- Builder cannot select two fields sharing a code across joined objects (execution throws — pre-existing R1 limit); filters/sorts on joined-object fields are primary-only.
- Scheduling honors `Frequency` only; `CronExpression` is stored but not evaluated this increment.
- SIF mapping is column-code-based; a report must expose the WPS field codes. Bank-specific SIF layouts remain separate `IBankProfile`s (future).
- Scheduled email actually sends only when an SMTP sender drains `EmailNotificationQueue` (pre-existing platform condition); the runner's contract ends at enqueue + stored file.

## Self-review
- No placeholders/TBD. Each sub-project has explicit files, interfaces, and tests.
- Internally consistent: all three reuse existing services; only additive code; no migration.
- Scope: three small, independently shippable increments — each maps cleanly to one implementation plan section.
- Ambiguity resolved: scheduled delivery = stored-file + email link (not attachment); SIF = profile mapping via existing bank pipeline (not a new `ExportFormat`); builder persists per step (not one big submit).
