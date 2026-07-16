# Reports Builder/Viewer + Scheduling + WPS/SIF — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the three remaining Reports gaps — a builder wizard + result viewer UI, an in-process scheduled-delivery runner, and a WPS/SIF export — all reusing existing backend services with **no DB migration**.

**Architecture:** Sub-project A is pure Next.js wired to the already-complete report CRUD/run endpoints. Sub-project B adds a `BackgroundService` + runner that runs due `ReportSchedule` rows, stores the export as a `StoredFile`, and enqueues `EmailNotificationQueue` rows with a download link. Sub-project C maps a report's rows through the existing `SaudiWpsSifProfile`/`BankFieldMapper` bank pipeline to produce a WPS SIF file.

**Tech Stack:** .NET 8, EF Core 8, MediatR, xUnit + FluentAssertions, QuestPDF (already referenced); Next.js 16.2.6 App Router (RTL, Thamania editorial tokens), TypeScript.

## Global Constraints

- **No DB migration in any task** — all tables/columns already exist.
- **Reuse, do not fork:** report CRUD/run/export endpoints, `IObjectCatalogService` catalog, `IReportExportService`, `SaudiWpsSifProfile`/`BankFieldMapper`/`SaudiWpsSifValidator`, `EmailNotificationQueue`, `StoredFile`, the `DocumentExpiryHostedService` pattern.
- **Enums (verbatim):** `ExportFormat { Excel=1, Csv=2, Txt=3, Xml=4, Pdf=5 }` (in `HR.Application.Engines.Finance.Export`). `ReportScheduleFrequency { Daily=1, Weekly=2, Monthly=3, Quarterly=4 }`. `ReportFieldType { ObjectField=1, CalculatedField=2, AggregateField=3, RelationshipField=4 }`. `ReportFilterOperator { Equals=1, NotEquals=2, Contains=3, StartsWith=4, EndsWith=5, ... }`.
- **DB-touching tests** are `[SkippableFact]` gated on env `REPORTS_TEST_DB`; pure logic gets DB-free xUnit tests. Commit after each task (`feat(reports):` / `test(reports):` / `fix(reports):`).
- **Frontend has no test runner** — the FE gate is `next build` green + the component compiles and follows the existing `usePermission`/`apiFetch` patterns. Backend gate: `dotnet build backend/src/HR.Api/HR.Api.csproj` = 0 errors and `dotnet test backend/tests/HR.Modules.Platform.Tests` green.
- **Backend export DTO (exact):** `ReportExportFile(byte[] Content, string ContentType, string FileName)` in `HR.Modules.Platform.Services.Reports`.
- **Catalog DTO (exact):** `CatalogObjectDto { Code, NameEn, NameAr, Module, Icon?, HasTenantScope, HasSoftDelete, HasDateCreated, FieldCount, List<CatalogFieldDto> Fields }`; `CatalogFieldDto { Code, NameEn, NameAr, FieldType, IsMeasure, IsGroupable, IsFilterable, IsDate, IsReference, ReferenceObjectCode?, List<EnumOptionDto>? Options }`. Catalog endpoint `GET /api/platform/registry/objects` is gated by `Platform.Dashboards.View` (NOT a reports permission).
- **Deploy once** after B+C: `dotnet publish backend/src/HR.Api -c Release -o <dir>`, zip with **forward-slash** entries (Python `zipfile`, never `Compress-Archive`), `az webapp deploy --resource-group HR --name hrcloud-api-v4xd --src-path <zip> --type zip`. Frontend auto-deploys to Vercel on push to `main`.

---

# SUB-PROJECT A — Builder + Viewer UI (frontend only)

## Task A1: Extend the reports API client with CRUD, run, and catalog calls

**Files:**
- Modify: `src/lib/api/reports.ts`

**Interfaces:**
- Consumes: `apiFetch`, `API_BASE_URL`, `getAccessToken` (already imported).
- Produces (used by A2/A3/B4/C3): types `ReportField`, `ReportFilter`, `ReportGrouping`, `ReportSorting`, `ReportDetail`, `ReportResult`, `ReportColumn`, `ReportGroup`, `CatalogObject`, `CatalogField`, `ReportSchedule`; functions `getReport(id)`, `createReport(body)`, `updateReport(id, body)`, `deleteReport(id)`, `publishReport(id)`, `runReport(id, page, pageSize)`, `addField/deleteField`, `addFilter/deleteFilter`, `addGrouping/deleteGrouping`, `addSorting/deleteSorting`, `getCatalogObjects()`, `getObjectFields(code)`, `getSchedules(id)`, `addSchedule(id, body)`, `deleteSchedule(scheduleId)`.

- [ ] **Step 1: Append the new types and functions** to `src/lib/api/reports.ts` (keep the existing `ReportDefinition`/`getReports`/`exportReport`/`ExportFormat`):

```typescript
// ── Detail + child types (mirror the backend DTOs) ──
export interface ReportField {
  id: string; fieldType: string; objectDefinitionId?: string | null;
  fieldCode: string; displayNameEn: string; displayNameAr: string;
  aggregation?: string | null; calculationExpression?: string | null;
  formatPattern?: string | null; width: number; sortOrder: number; isVisible: boolean;
}
export interface ReportFilter {
  id: string; fieldCode: string; operator: string; value?: string | null;
  valueTo?: string | null; logicalOperator?: string | null; isParameter: boolean;
}
export interface ReportGrouping { id: string; fieldCode: string; sortOrder: number; }
export interface ReportSorting { id: string; fieldCode: string; direction: string; sortOrder: number; }

export interface ReportDetail extends ReportDefinition {
  primaryObjectId: string;
  fields: ReportField[];
  filters: ReportFilter[];
  groupings?: ReportGrouping[];
  sortings?: ReportSorting[];
}

// ── Run result (mirror ReportResult / ReportColumn / ReportGroup) ──
export interface ReportColumn { code: string; label: string; type: string; isMeasure: boolean; aggregation?: string | null; formatPattern?: string | null; }
export interface ReportGroup { fieldCode: string; key: unknown; label: string; subGroups: ReportGroup[]; rows: Record<string, unknown>[]; aggregates: Record<string, number>; count: number; }
export interface ReportResult {
  reportCode: string; columns: ReportColumn[]; groups: ReportGroup[];
  rows: Record<string, unknown>[]; grandTotals: Record<string, number>;
  totalCount: number; page: number; pageSize: number; truncated: boolean;
}

// ── Object catalog (mirror CatalogObjectDto / CatalogFieldDto) ──
export interface CatalogField {
  code: string; nameEn: string; nameAr: string; fieldType: string;
  isMeasure: boolean; isGroupable: boolean; isFilterable: boolean; isDate: boolean;
  isReference: boolean; referenceObjectCode?: string | null;
  options?: { value: number; label: string }[] | null;
}
export interface CatalogObject {
  code: string; nameEn: string; nameAr: string; module: string; icon?: string | null;
  fieldCount: number; fields: CatalogField[];
}

// ── Schedules (mirror ReportScheduleDto) ──
export interface ReportSchedule {
  id: string; frequency: string; cronExpression?: string | null;
  exportFormat: string; recipients: string; isActive: boolean;
  lastRunAt?: string | null; nextRunAt?: string | null;
}

export interface CreateReportBody {
  code: string; nameEn: string; nameAr: string; description?: string;
  reportType: number; scope: number; primaryObjectId: string;
}

export const getReport = (id: string) => apiFetch<ReportDetail>(`/api/platform/reports/${id}`);
export const createReport = (body: CreateReportBody) =>
  apiFetch<ReportDetail>(`/api/platform/reports`, { method: "POST", body: JSON.stringify(body) });
export const updateReport = (id: string, body: Omit<CreateReportBody, "code" | "primaryObjectId">) =>
  apiFetch<ReportDetail>(`/api/platform/reports/${id}`, { method: "PUT", body: JSON.stringify({ id, ...body }) });
export const deleteReport = (id: string) =>
  apiFetch<unknown>(`/api/platform/reports/${id}`, { method: "DELETE" });
export const publishReport = (id: string) =>
  apiFetch<ReportDetail>(`/api/platform/reports/${id}/publish`, { method: "POST" });
export const runReport = (id: string, page = 1, pageSize = 50) =>
  apiFetch<ReportResult>(`/api/platform/reports/${id}/run?page=${page}&pageSize=${pageSize}`, { method: "POST" });

export const addField = (id: string, body: Record<string, unknown>) =>
  apiFetch<ReportField>(`/api/platform/reports/${id}/fields`, { method: "POST", body: JSON.stringify(body) });
export const deleteField = (fieldId: string) =>
  apiFetch<unknown>(`/api/platform/reports/fields/${fieldId}`, { method: "DELETE" });
export const addFilter = (id: string, body: Record<string, unknown>) =>
  apiFetch<ReportFilter>(`/api/platform/reports/${id}/filters`, { method: "POST", body: JSON.stringify(body) });
export const deleteFilter = (filterId: string) =>
  apiFetch<unknown>(`/api/platform/reports/filters/${filterId}`, { method: "DELETE" });
export const addGrouping = (id: string, body: Record<string, unknown>) =>
  apiFetch<ReportGrouping>(`/api/platform/reports/${id}/groupings`, { method: "POST", body: JSON.stringify(body) });
export const deleteGrouping = (groupingId: string) =>
  apiFetch<unknown>(`/api/platform/reports/groupings/${groupingId}`, { method: "DELETE" });
export const addSorting = (id: string, body: Record<string, unknown>) =>
  apiFetch<ReportSorting>(`/api/platform/reports/${id}/sortings`, { method: "POST", body: JSON.stringify(body) });
export const deleteSorting = (sortingId: string) =>
  apiFetch<unknown>(`/api/platform/reports/sortings/${sortingId}`, { method: "DELETE" });

export const getCatalogObjects = () => apiFetch<CatalogObject[]>(`/api/platform/registry/objects`);
export const getObjectFields = (code: string) => apiFetch<CatalogField[]>(`/api/platform/registry/objects/${code}/fields`);

export const getSchedules = (id: string) => apiFetch<ReportSchedule[]>(`/api/platform/reports/${id}/schedules`);
export const addSchedule = (id: string, body: Record<string, unknown>) =>
  apiFetch<ReportSchedule>(`/api/platform/reports/${id}/schedules`, { method: "POST", body: JSON.stringify(body) });
export const deleteSchedule = (scheduleId: string) =>
  apiFetch<unknown>(`/api/platform/reports/schedules/${scheduleId}`, { method: "DELETE" });
```

> Note: confirm `apiFetch`'s signature by opening `src/lib/api-client.ts`. If it unwraps the `ApiResponse<T>` envelope and returns `data`, the generics above are correct. If not, adjust to unwrap `.data`. The list page already uses `apiFetch<PaginatedReports>` successfully, so the envelope handling is established — match it.

- [ ] **Step 2: Verify the build compiles**

Run: `npm run build` (or `npx next build`)
Expected: TypeScript compiles with 0 errors (the new exports are unused so far — that's fine).

- [ ] **Step 3: Commit**

```bash
git add src/lib/api/reports.ts
git commit -m "feat(reports): API client for report CRUD, run, catalog, schedules"
```

---

## Task A2: Shared result table + Viewer page

**Files:**
- Create: `src/components/reports/report-table.tsx`
- Create: `src/app/(dashboard)/reports/[id]/page.tsx`

**Interfaces:**
- Consumes: `ReportResult`, `ReportColumn`, `ReportGroup`, `runReport`, `getReport`, `exportReport`, `ExportFormat` from A1.
- Produces: `ReportTable` component `({ result }: { result: ReportResult })` used by A2 viewer and A3 builder preview.

- [ ] **Step 1: Create the shared table** `src/components/reports/report-table.tsx`:

```tsx
"use client";

import { ReportResult, ReportColumn, ReportGroup } from "@/lib/api/reports";

function fmt(v: unknown): string {
  if (v === null || v === undefined) return "";
  if (typeof v === "number") return v.toLocaleString();
  return String(v);
}

function GroupRows({ group, columns, depth }: { group: ReportGroup; columns: ReportColumn[]; depth: number }) {
  return (
    <>
      <tr className="bg-secondary/60">
        <td colSpan={columns.length} className="px-4 py-2 font-semibold" style={{ paddingInlineStart: 16 + depth * 16 }}>
          {group.label}
        </td>
      </tr>
      {group.subGroups?.length
        ? group.subGroups.map((g, i) => <GroupRows key={i} group={g} columns={columns} depth={depth + 1} />)
        : group.rows.map((row, i) => (
            <tr key={i} className="border-b border-border/60">
              {columns.map((c) => (
                <td key={c.code} className={`px-4 py-2 ${c.isMeasure ? "text-left tabular-nums" : ""}`}>{fmt(row[c.code])}</td>
              ))}
            </tr>
          ))}
      <tr className="border-b border-border bg-secondary/30 text-sm font-medium">
        {columns.map((c, idx) => (
          <td key={c.code} className={`px-4 py-1.5 ${c.isMeasure ? "text-left tabular-nums" : ""}`}>
            {idx === 0 ? `${group.label} — إجمالي` : c.isMeasure ? fmt(group.aggregates?.[c.code]) : ""}
          </td>
        ))}
      </tr>
    </>
  );
}

export function ReportTable({ result }: { result: ReportResult }) {
  const cols = result.columns;
  const grouped = result.groups?.length > 0;
  return (
    <div className="border border-border bg-card overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-border text-right text-xs uppercase tracking-wider text-muted-foreground">
            {cols.map((c) => (
              <th key={c.code} className={`px-4 py-3 font-medium ${c.isMeasure ? "text-left" : ""}`}>{c.label}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {grouped
            ? result.groups.map((g, i) => <GroupRows key={i} group={g} columns={cols} depth={0} />)
            : result.rows.map((row, i) => (
                <tr key={i} className="border-b border-border/60 hover:bg-secondary/40">
                  {cols.map((c) => (
                    <td key={c.code} className={`px-4 py-2 ${c.isMeasure ? "text-left tabular-nums" : ""}`}>{fmt(row[c.code])}</td>
                  ))}
                </tr>
              ))}
          {Object.keys(result.grandTotals ?? {}).length > 0 && (
            <tr className="border-t-2 border-border bg-secondary/50 font-semibold">
              {cols.map((c, idx) => (
                <td key={c.code} className={`px-4 py-2 ${c.isMeasure ? "text-left tabular-nums" : ""}`}>
                  {idx === 0 ? "الإجمالي العام" : c.isMeasure ? fmt(result.grandTotals[c.code]) : ""}
                </td>
              ))}
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
```

- [ ] **Step 2: Create the viewer page** `src/app/(dashboard)/reports/[id]/page.tsx`:

```tsx
"use client";

import { use, useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { AlertTriangle, Loader2, Pencil, RefreshCw } from "lucide-react";
import { toast } from "sonner";
import { usePermission } from "@/lib/permissions";
import { getReport, runReport, exportReport, ReportDetail, ReportResult, ExportFormat } from "@/lib/api/reports";
import { ReportTable } from "@/components/reports/report-table";

const FORMATS: { key: ExportFormat; label: string }[] = [
  { key: "excel", label: "Excel" }, { key: "csv", label: "CSV" }, { key: "pdf", label: "PDF" },
];

export default function ReportViewerPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { allowed: canEdit } = usePermission("Platform.Reports.Edit");
  const { allowed: canExport } = usePermission("Platform.Reports.Export");
  const [report, setReport] = useState<ReportDetail | null>(null);
  const [result, setResult] = useState<ReportResult | null>(null);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [r, res] = await Promise.all([getReport(id), runReport(id, page, 50)]);
      setReport(r); setResult(res);
    } catch { toast.error("تعذر تحميل التقرير"); }
    finally { setLoading(false); }
  }, [id, page]);

  useEffect(() => { queueMicrotask(() => { load(); }); }, [load]);

  const doExport = async (f: ExportFormat) => {
    setBusy(f);
    try { await exportReport(id, f, report?.code || "report"); toast.success("تم بدء التنزيل"); }
    catch { /* toast surfaced in exportReport */ }
    finally { setBusy(null); }
  };

  return (
    <div className="space-y-6" dir="rtl">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold">{report?.nameAr || report?.nameEn || "تقرير"}</h1>
          <p className="text-sm text-muted-foreground mt-1">{report?.nameEn}{report?.code ? ` · ${report.code}` : ""}</p>
        </div>
        <div className="flex items-center gap-2">
          {canEdit && report && (
            <Link href={`/reports/builder/${id}`} className="inline-flex h-9 items-center gap-2 border border-border bg-secondary px-3 text-sm hover:bg-secondary/70">
              <Pencil className="h-4 w-4" /> تعديل
            </Link>
          )}
          <button onClick={load} className="inline-flex h-9 items-center gap-2 border border-border bg-secondary px-3 text-sm hover:bg-secondary/70">
            <RefreshCw className="h-4 w-4" /> تحديث
          </button>
        </div>
      </div>

      {canExport && (
        <div className="flex items-center gap-1.5">
          {FORMATS.map((f) => (
            <button key={f.key} onClick={() => doExport(f.key)} disabled={busy !== null}
              className="inline-flex h-8 items-center gap-1.5 border border-border bg-secondary px-2.5 text-xs hover:bg-secondary/70 disabled:opacity-50">
              {busy === f.key ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : null} {f.label}
            </button>
          ))}
        </div>
      )}

      {result?.truncated && (
        <div className="flex items-center gap-2 border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-sm text-amber-700 dark:text-amber-400">
          <AlertTriangle className="h-4 w-4" /> يتم عرض جزء من النتائج فقط. أضِف عوامل تصفية لتضييق النطاق.
        </div>
      )}

      {loading ? (
        <div className="border border-border bg-card p-12 flex items-center justify-center">
          <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
        </div>
      ) : result ? (
        <>
          <ReportTable result={result} />
          {!result.groups?.length && result.totalCount > result.pageSize && (
            <div className="flex items-center justify-between text-sm">
              <button disabled={page <= 1} onClick={() => setPage((p) => p - 1)} className="border border-border px-3 py-1 disabled:opacity-40">السابق</button>
              <span className="text-muted-foreground">صفحة {result.page} — {result.totalCount} سجل</span>
              <button disabled={result.page * result.pageSize >= result.totalCount} onClick={() => setPage((p) => p + 1)} className="border border-border px-3 py-1 disabled:opacity-40">التالي</button>
            </div>
          )}
        </>
      ) : null}
    </div>
  );
}
```

> Note: confirm the Next.js 16 page-params convention in this repo by opening any existing `src/app/(dashboard)/**/[id]/page.tsx`. The repo's AGENTS.md warns params/APIs may differ — if pages here receive `params` synchronously (not a Promise), drop the `use(params)` wrapper and read `params.id` directly. Match the sibling pages.

- [ ] **Step 3: Verify the build** — `npx next build` → 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/components/reports/report-table.tsx "src/app/(dashboard)/reports/[id]/page.tsx"
git commit -m "feat(reports): result viewer page + shared grouped table with subtotals"
```

---

## Task A3: Builder wizard

**Files:**
- Create: `src/app/(dashboard)/reports/builder/[[...id]]/page.tsx`

**Interfaces:**
- Consumes: everything from A1 + `ReportTable` from A2.
- Produces: the create/edit UI (no exports consumed elsewhere).

- [ ] **Step 1: Create the wizard page** `src/app/(dashboard)/reports/builder/[[...id]]/page.tsx`. It persists per step (create-on-step-1, then granular add/delete). Full component:

```tsx
"use client";

import { use, useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { Loader2, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import {
  getReport, createReport, updateReport, publishReport, runReport,
  addField, deleteField, addFilter, deleteFilter, addGrouping, deleteGrouping,
  addSorting, deleteSorting, getCatalogObjects, getObjectFields,
  ReportDetail, ReportResult, CatalogObject, CatalogField,
} from "@/lib/api/reports";
import { ReportTable } from "@/components/reports/report-table";

const REPORT_TYPES = [{ v: 1, l: "جدولي" }, { v: 2, l: "ملخّص" }, { v: 3, l: "مصفوفة" }];
const SCOPES = [{ v: 1, l: "شخصي" }, { v: 2, l: "الشركة" }, { v: 3, l: "قسم" }];
const OPERATORS = [{ v: 1, l: "يساوي" }, { v: 2, l: "لا يساوي" }, { v: 3, l: "يحتوي" }, { v: 4, l: "يبدأ بـ" }, { v: 5, l: "ينتهي بـ" }];

export default function ReportBuilderPage({ params }: { params: Promise<{ id?: string[] }> }) {
  const { id: idParam } = use(params);
  const router = useRouter();
  const existingId = idParam?.[0];

  const [step, setStep] = useState(0);
  const [reportId, setReportId] = useState<string | undefined>(existingId);
  const [report, setReport] = useState<ReportDetail | null>(null);
  const [objects, setObjects] = useState<CatalogObject[]>([]);
  const [fields, setFields] = useState<CatalogField[]>([]);
  const [saving, setSaving] = useState(false);
  const [preview, setPreview] = useState<ReportResult | null>(null);

  // basics form
  const [form, setForm] = useState({ code: "", nameEn: "", nameAr: "", description: "", reportType: 1, scope: 2, primaryObjectId: "" });

  useEffect(() => { queueMicrotask(async () => {
    try { setObjects(await getCatalogObjects()); } catch { toast.error("تعذر تحميل الكائنات"); }
    if (existingId) {
      const r = await getReport(existingId);
      setReport(r);
      setForm({ code: r.code, nameEn: r.nameEn, nameAr: r.nameAr, description: r.description ?? "", reportType: Number(r.reportType) || 1, scope: Number(r.scope) || 2, primaryObjectId: r.primaryObjectId });
    }
  }); }, [existingId]);

  // Load catalog fields for the chosen object (by code). The catalog objects carry a Code; the
  // report stores PrimaryObjectId (a registry Guid). We match on the object whose code we picked.
  const [primaryObjectCode, setPrimaryObjectCode] = useState<string>("");
  useEffect(() => { if (!primaryObjectCode) return; queueMicrotask(async () => {
    try { setFields(await getObjectFields(primaryObjectCode)); } catch { /* ignore */ }
  }); }, [primaryObjectCode]);

  const refreshReport = useCallback(async () => {
    if (reportId) setReport(await getReport(reportId));
  }, [reportId]);

  const saveBasics = async () => {
    setSaving(true);
    try {
      if (reportId) {
        await updateReport(reportId, { nameEn: form.nameEn, nameAr: form.nameAr, description: form.description, reportType: form.reportType, scope: form.scope });
      } else {
        const created = await createReport(form);
        setReportId(created.id); setReport(created);
      }
      setStep(1);
    } catch { toast.error("تعذّر حفظ التقرير"); }
    finally { setSaving(false); }
  };

  const runPreview = async () => {
    if (!reportId) return;
    try { setPreview(await runReport(reportId, 1, 50)); }
    catch { toast.error("تعذّر تشغيل المعاينة"); }
  };

  const steps = ["الأساسيات", "الحقول", "عوامل التصفية", "التجميع والفرز", "المعاينة"];

  return (
    <div className="space-y-6" dir="rtl">
      <h1 className="text-2xl font-bold">{existingId ? "تعديل تقرير" : "تقرير جديد"}</h1>

      <ol className="flex flex-wrap gap-2 text-sm">
        {steps.map((s, i) => (
          <li key={i}>
            <button disabled={!reportId && i > 0} onClick={() => setStep(i)}
              className={`border px-3 py-1 ${i === step ? "border-primary bg-primary/10 text-primary" : "border-border bg-secondary"} disabled:opacity-40`}>
              {i + 1}. {s}
            </button>
          </li>
        ))}
      </ol>

      {/* Step 0 — Basics */}
      {step === 0 && (
        <div className="space-y-4 border border-border bg-card p-6 max-w-2xl">
          <Field label="الكود"><input disabled={!!reportId} value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} className="input" /></Field>
          <Field label="الاسم (عربي)"><input value={form.nameAr} onChange={(e) => setForm({ ...form, nameAr: e.target.value })} className="input" /></Field>
          <Field label="الاسم (إنجليزي)"><input value={form.nameEn} onChange={(e) => setForm({ ...form, nameEn: e.target.value })} className="input" /></Field>
          <Field label="الوصف"><input value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} className="input" /></Field>
          <Field label="النوع">
            <select value={form.reportType} onChange={(e) => setForm({ ...form, reportType: Number(e.target.value) })} className="input">
              {REPORT_TYPES.map((t) => <option key={t.v} value={t.v}>{t.l}</option>)}
            </select>
          </Field>
          <Field label="النطاق">
            <select value={form.scope} onChange={(e) => setForm({ ...form, scope: Number(e.target.value) })} className="input">
              {SCOPES.map((t) => <option key={t.v} value={t.v}>{t.l}</option>)}
            </select>
          </Field>
          {!reportId && (
            <Field label="الكائن الأساسي">
              <select value={form.primaryObjectId} onChange={(e) => { const o = objects.find((x) => x.code === e.target.value); setForm({ ...form, primaryObjectId: o ? o.code : "" }); setPrimaryObjectCode(e.target.value); }} className="input">
                <option value="">— اختر —</option>
                {objects.map((o) => <option key={o.code} value={o.code}>{o.nameAr || o.nameEn}</option>)}
              </select>
              <p className="text-xs text-muted-foreground mt-1">ملاحظة: الكائن الأساسي يُحدَّد مرة واحدة عند الإنشاء.</p>
            </Field>
          )}
          <button onClick={saveBasics} disabled={saving || (!reportId && (!form.code || !form.primaryObjectId))} className="btn-primary">
            {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : null} حفظ ومتابعة
          </button>
        </div>
      )}

      {/* Step 1 — Fields */}
      {step === 1 && reportId && (
        <FieldsStep report={report} fields={fields} reportId={reportId} onChange={refreshReport} loadCode={setPrimaryObjectCode} />
      )}

      {/* Step 2 — Filters */}
      {step === 2 && reportId && (
        <ChildList
          title="عوامل التصفية"
          items={(report?.filters ?? []).map((f) => ({ id: f.id, label: `${f.fieldCode} ${f.operator} ${f.value ?? ""}` }))}
          onDelete={async (fid) => { await deleteFilter(fid); await refreshReport(); }}
          adder={<FilterAdder fields={report?.fields ?? []} onAdd={async (b) => { await addFilter(reportId, b); await refreshReport(); }} />}
        />
      )}

      {/* Step 3 — Grouping + Sorting */}
      {step === 3 && reportId && (
        <div className="grid gap-6 md:grid-cols-2">
          <ChildList
            title="التجميع"
            items={(report?.groupings ?? []).map((g) => ({ id: g.id, label: g.fieldCode }))}
            onDelete={async (gid) => { await deleteGrouping(gid); await refreshReport(); }}
            adder={<CodePicker fields={report?.fields ?? []} label="أضف تجميعًا" onPick={async (code) => { await addGrouping(reportId, { fieldCode: code, sortOrder: 0 }); await refreshReport(); }} />}
          />
          <ChildList
            title="الفرز"
            items={(report?.sortings ?? []).map((s) => ({ id: s.id, label: `${s.fieldCode} · ${s.direction}` }))}
            onDelete={async (sid) => { await deleteSorting(sid); await refreshReport(); }}
            adder={<CodePicker fields={report?.fields ?? []} label="أضف فرزًا" onPick={async (code) => { await addSorting(reportId, { fieldCode: code, direction: 1, sortOrder: 0 }); await refreshReport(); }} />}
          />
        </div>
      )}

      {/* Step 4 — Preview */}
      {step === 4 && reportId && (
        <div className="space-y-4">
          <div className="flex items-center gap-2">
            <button onClick={runPreview} className="btn-primary">تشغيل المعاينة</button>
            <button onClick={async () => { await publishReport(reportId); toast.success("تم النشر"); }} className="border border-border bg-secondary px-4 py-2 text-sm">نشر</button>
            <button onClick={() => router.push(`/reports/${reportId}`)} className="border border-border bg-secondary px-4 py-2 text-sm">فتح العارض</button>
          </div>
          {preview && <ReportTable result={preview} />}
        </div>
      )}
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label className="block"><span className="text-sm font-medium">{label}</span><div className="mt-1">{children}</div></label>;
}

function FieldsStep({ report, fields, reportId, onChange, loadCode }: { report: ReportDetail | null; fields: CatalogField[]; reportId: string; onChange: () => Promise<void>; loadCode: (c: string) => void }) {
  // If fields haven't loaded (edit mode), let the user trigger a catalog load by object code.
  return (
    <div className="grid gap-6 md:grid-cols-2">
      <div className="border border-border bg-card p-4">
        <h3 className="font-semibold mb-3">الحقول المتاحة</h3>
        {fields.length === 0 && <p className="text-sm text-muted-foreground">لا توجد حقول محمّلة. ارجع للأساسيات لاختيار الكائن.</p>}
        <ul className="space-y-1 max-h-96 overflow-auto">
          {fields.map((f) => (
            <li key={f.code} className="flex items-center justify-between">
              <span className="text-sm">{f.nameAr || f.nameEn}</span>
              <button className="text-primary" title="أضف"
                onClick={async () => { await addField(reportId, { fieldType: f.isMeasure ? 3 : 1, fieldCode: f.code, displayNameEn: f.nameEn, displayNameAr: f.nameAr, aggregation: f.isMeasure ? 1 : null, width: 120, sortOrder: (report?.fields.length ?? 0) }); await onChange(); }}>
                <Plus className="h-4 w-4" />
              </button>
            </li>
          ))}
        </ul>
      </div>
      <div className="border border-border bg-card p-4">
        <h3 className="font-semibold mb-3">الحقول المختارة</h3>
        <ul className="space-y-1">
          {(report?.fields ?? []).map((f) => (
            <li key={f.id} className="flex items-center justify-between">
              <span className="text-sm">{f.displayNameAr || f.displayNameEn}{f.aggregation ? ` (${f.aggregation})` : ""}</span>
              <button className="text-destructive" onClick={async () => { await deleteField(f.id); await onChange(); }}><Trash2 className="h-4 w-4" /></button>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}

function ChildList({ title, items, onDelete, adder }: { title: string; items: { id: string; label: string }[]; onDelete: (id: string) => Promise<void>; adder: React.ReactNode }) {
  return (
    <div className="border border-border bg-card p-4 space-y-3">
      <h3 className="font-semibold">{title}</h3>
      <ul className="space-y-1">
        {items.map((it) => (
          <li key={it.id} className="flex items-center justify-between text-sm">
            <span>{it.label}</span>
            <button className="text-destructive" onClick={() => onDelete(it.id)}><Trash2 className="h-4 w-4" /></button>
          </li>
        ))}
      </ul>
      {adder}
    </div>
  );
}

function CodePicker({ fields, label, onPick }: { fields: ReportDetail["fields"]; label: string; onPick: (code: string) => Promise<void> }) {
  const [code, setCode] = useState("");
  return (
    <div className="flex items-center gap-2">
      <select value={code} onChange={(e) => setCode(e.target.value)} className="input flex-1">
        <option value="">— حقل —</option>
        {fields.map((f) => <option key={f.id} value={f.fieldCode}>{f.displayNameAr || f.displayNameEn}</option>)}
      </select>
      <button disabled={!code} onClick={() => code && onPick(code)} className="btn-primary">{label}</button>
    </div>
  );
}

function FilterAdder({ fields, onAdd }: { fields: ReportDetail["fields"]; onAdd: (b: Record<string, unknown>) => Promise<void> }) {
  const [code, setCode] = useState(""); const [op, setOp] = useState(1); const [value, setValue] = useState("");
  return (
    <div className="flex flex-wrap items-center gap-2">
      <select value={code} onChange={(e) => setCode(e.target.value)} className="input">
        <option value="">— حقل —</option>
        {fields.map((f) => <option key={f.id} value={f.fieldCode}>{f.displayNameAr || f.displayNameEn}</option>)}
      </select>
      <select value={op} onChange={(e) => setOp(Number(e.target.value))} className="input">
        {OPERATORS.map((o) => <option key={o.v} value={o.v}>{o.l}</option>)}
      </select>
      <input value={value} onChange={(e) => setValue(e.target.value)} placeholder="القيمة" className="input" />
      <button disabled={!code} onClick={() => code && onAdd({ fieldCode: code, operator: op, value })} className="btn-primary">أضف</button>
    </div>
  );
}
```

> Note: `input`, `btn-primary` are used as shorthand classes — replace with the project's actual utility classes if these aren't defined in `globals.css`. Check an existing form page (e.g. a settings page) and mirror its input/button classes. If none exist, inline the Tailwind (`className="h-9 w-full border border-border bg-background px-3 text-sm"` for inputs; `"inline-flex h-9 items-center gap-2 bg-primary px-4 text-sm text-primary-foreground"` for the primary button).

- [ ] **Step 2: Verify the build** — `npx next build` → 0 errors.

- [ ] **Step 3: Commit**

```bash
git add "src/app/(dashboard)/reports/builder"
git commit -m "feat(reports): 5-step report builder wizard (basics/fields/filters/group-sort/preview)"
```

---

## Task A4: List page — New / Open / Edit actions

**Files:**
- Modify: `src/app/(dashboard)/reports/page.tsx`

- [ ] **Step 1:** Add a permission hook and a "New report" button + row actions. In `reports/page.tsx`:

Add near the other `usePermission` call:
```tsx
const { allowed: canCreate } = usePermission("Platform.Reports.Create");
const { allowed: canEdit } = usePermission("Platform.Reports.Edit");
```

Add `import Link from "next/link";` and a **New report** button in the header block (next to تحديث):
```tsx
{canCreate && (
  <Link href="/reports/builder" className="inline-flex h-9 items-center gap-2 bg-primary px-3 text-sm text-primary-foreground hover:bg-primary/90">
    + تقرير جديد
  </Link>
)}
```

Wrap the report name cell so it links to the viewer, and add an Edit link in the actions column:
```tsx
<td className="px-4 py-3">
  <Link href={`/reports/${r.id}`} className="font-medium hover:underline">{r.nameAr || r.nameEn}</Link>
  <div className="text-xs text-muted-foreground">{r.nameEn}{r.code ? ` · ${r.code}` : ""}</div>
</td>
```
And in the export cell, prepend (before the format buttons):
```tsx
{canEdit && (
  <Link href={`/reports/builder/${r.id}`} className="inline-flex h-8 items-center gap-1.5 border border-border bg-secondary px-2.5 text-xs hover:bg-secondary/70">تعديل</Link>
)}
```

- [ ] **Step 2: Verify the build** — `npx next build` → 0 errors.

- [ ] **Step 3: Commit**

```bash
git add "src/app/(dashboard)/reports/page.tsx"
git commit -m "feat(reports): list page New/Open/Edit actions wired to builder+viewer"
```

---

# SUB-PROJECT B — Scheduling runner + delivery (backend + UI)

## Task B1: `GetReportSchedulesQuery` + list endpoint

**Files:**
- Create: `backend/src/HR.Modules/Platform/Queries/Reports/ReportScheduleQueries.cs`
- Modify: `backend/src/HR.Modules/Platform/Controllers/ReportsController.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/ReportScheduleQueryTests.cs`

**Interfaces:**
- Produces: `record GetReportSchedulesQuery(Guid ReportDefinitionId) : IRequest<List<ReportScheduleDto>>` + handler; `GET api/platform/reports/{id}/schedules`.

- [ ] **Step 1: Write the query + handler** in `ReportScheduleQueries.cs`:

```csharp
using AutoMapper;
using AutoMapper.QueryableExtensions;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Queries.Reports;

public record GetReportSchedulesQuery(Guid ReportDefinitionId) : IRequest<List<ReportScheduleDto>>;

public class GetReportSchedulesQueryHandler : IRequestHandler<GetReportSchedulesQuery, List<ReportScheduleDto>>
{
    private readonly ApplicationDbContext _db; private readonly IMapper _mapper;
    public GetReportSchedulesQueryHandler(ApplicationDbContext db, IMapper mapper) { _db = db; _mapper = mapper; }

    public async Task<List<ReportScheduleDto>> Handle(GetReportSchedulesQuery request, CancellationToken ct) =>
        await _db.Set<ReportSchedule>()
            .Where(s => s.ReportDefinitionId == request.ReportDefinitionId)
            .OrderByDescending(s => s.CreatedAt)
            .ProjectTo<ReportScheduleDto>(_mapper.ConfigurationProvider)
            .ToListAsync(ct);
}
```

> Note: confirm `ReportScheduleDto` is mapped in `PlatformMappingProfile`. If `ProjectTo` fails at runtime for lack of a map, add `CreateMap<ReportSchedule, ReportScheduleDto>();` there. Also confirm `BaseEntity` exposes `CreatedAt`; if the property is named `DateCreated`, order by that instead.

- [ ] **Step 2: Add the endpoint** in `ReportsController.cs` (next to the other schedule endpoints):

```csharp
[HttpGet("{id:guid}/schedules")]
[RequirePermission("Platform.Reports.View")]
public async Task<ActionResult<ApiResponse<List<ReportScheduleDto>>>> GetSchedules(Guid id, CancellationToken ct)
{ var result = await Mediator.Send(new GetReportSchedulesQuery(id), ct); return OkResponse(result); }
```

- [ ] **Step 3: Write a `[SkippableFact]`** in `ReportScheduleQueryTests.cs` that seeds one `ReportSchedule` and asserts the query returns it. Mirror the `Conn`/`StubUser` harness from `ReportShareCommandTests.cs`:

```csharp
[SkippableFact]
public async Task Returns_schedules_for_report()
{
    Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
    var tenant = Guid.NewGuid(); var user = new StubUser(Guid.NewGuid(), tenant);
    var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Conn).Options;
    await using var db = new ApplicationDbContext(opts, user);
    await using var tx = await db.Database.BeginTransactionAsync();
    var reportId = Guid.NewGuid();
    db.Set<HR.Domain.Engines.Reports.ReportSchedule>().Add(new() { ReportDefinitionId = reportId, Frequency = HR.Domain.Enums.ReportScheduleFrequency.Daily, ExportFormat = HR.Application.Engines.Finance.Export.ExportFormat.Csv, Recipients = "[\"a@b.com\"]", IsActive = true });
    await db.SaveChangesAsync();
    var mapper = new AutoMapper.MapperConfiguration(c => c.AddProfile<HR.Modules.Platform.MappingProfiles.PlatformMappingProfile>()).CreateMapper();
    var res = await new GetReportSchedulesQueryHandler(db, mapper).Handle(new GetReportSchedulesQuery(reportId), default);
    res.Should().ContainSingle();
    await tx.RollbackAsync();
}
```

- [ ] **Step 4: Build + test** — `dotnet build backend/src/HR.Api/HR.Api.csproj` (0 errors); `dotnet test backend/tests/HR.Modules.Platform.Tests` (green; new test skipped locally).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Queries/Reports/ReportScheduleQueries.cs backend/src/HR.Modules/Platform/Controllers/ReportsController.cs backend/tests/HR.Modules.Platform.Tests/Reports/ReportScheduleQueryTests.cs
git commit -m "feat(reports): GET {id}/schedules list query + endpoint"
```

---

## Task B2: `ReportScheduleRunner` — due selection, NextRunAt math, delivery

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/Reports/IReportScheduleRunner.cs`
- Create: `backend/src/HR.Modules/Platform/Services/Reports/ReportScheduleRunner.cs`
- Modify: `backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/ReportScheduleRunnerTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`, `IReportExportService.ExportAsync` (returns `ReportExportFile(byte[] Content, string ContentType, string FileName)`), `IBackgroundExecutionContext` (sets tenant scope for background work — confirm its method by reading `HR.Application/Common/Interfaces/IBackgroundExecutionContext.cs`).
- Produces:
  - `static class ScheduleMath { DateTime ComputeNextRun(ReportScheduleFrequency freq, DateTime fromUtc); IReadOnlyList<string> ParseEmails(string recipientsJson); }`
  - `interface IReportScheduleRunner { Task<int> RunDueAsync(CancellationToken ct); }`

- [ ] **Step 1: Write DB-free unit tests** for the pure math in `ReportScheduleRunnerTests.cs`:

```csharp
using System;
using System.Linq;
using FluentAssertions;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ScheduleMathTests
{
    private static readonly DateTime Base = new(2026, 7, 16, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ReportScheduleFrequency.Daily, 1)]
    [InlineData(ReportScheduleFrequency.Weekly, 7)]
    public void ComputeNextRun_adds_days(ReportScheduleFrequency f, int days)
        => ScheduleMath.ComputeNextRun(f, Base).Should().Be(Base.AddDays(days));

    [Fact]
    public void ComputeNextRun_monthly_adds_month()
        => ScheduleMath.ComputeNextRun(ReportScheduleFrequency.Monthly, Base).Should().Be(Base.AddMonths(1));

    [Fact]
    public void ComputeNextRun_quarterly_adds_three_months()
        => ScheduleMath.ComputeNextRun(ReportScheduleFrequency.Quarterly, Base).Should().Be(Base.AddMonths(3));

    [Fact]
    public void ParseEmails_extracts_only_addresses()
    {
        var emails = ScheduleMath.ParseEmails("[\"a@b.com\", \"not-an-email\", \"c@d.com\"]");
        emails.Should().BeEquivalentTo(new[] { "a@b.com", "c@d.com" });
    }

    [Fact]
    public void ParseEmails_tolerates_garbage()
        => ScheduleMath.ParseEmails("not json").Should().BeEmpty();
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~ScheduleMathTests` → FAIL (type missing).

- [ ] **Step 3: Implement** `IReportScheduleRunner.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace HR.Modules.Platform.Services.Reports;

public interface IReportScheduleRunner
{
    Task<int> RunDueAsync(CancellationToken ct);
}
```

And `ReportScheduleRunner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HR.Domain.Engines.Files;
using HR.Domain.Engines.Notifications;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Pure scheduling helpers — unit-tested without a DB.</summary>
public static class ScheduleMath
{
    public static DateTime ComputeNextRun(ReportScheduleFrequency freq, DateTime fromUtc) => freq switch
    {
        ReportScheduleFrequency.Daily => fromUtc.AddDays(1),
        ReportScheduleFrequency.Weekly => fromUtc.AddDays(7),
        ReportScheduleFrequency.Monthly => fromUtc.AddMonths(1),
        ReportScheduleFrequency.Quarterly => fromUtc.AddMonths(3),
        _ => fromUtc.AddDays(1),
    };

    public static IReadOnlyList<string> ParseEmails(string recipientsJson)
    {
        if (string.IsNullOrWhiteSpace(recipientsJson)) return Array.Empty<string>();
        try
        {
            var arr = JsonSerializer.Deserialize<List<string>>(recipientsJson) ?? new();
            return arr.Where(s => !string.IsNullOrWhiteSpace(s) && s.Contains('@')).ToList();
        }
        catch (JsonException) { return Array.Empty<string>(); }
    }
}

/// <summary>Runs due report schedules: export → store file → enqueue email(s) with a download link,
/// then stamp LastRunAt and roll NextRunAt forward. One schedule failing does not abort the batch.</summary>
public sealed class ReportScheduleRunner : IReportScheduleRunner
{
    private readonly ApplicationDbContext _db;
    private readonly IReportExportService _export;
    private readonly ILogger<ReportScheduleRunner> _logger;

    public ReportScheduleRunner(ApplicationDbContext db, IReportExportService export, ILogger<ReportScheduleRunner> logger)
    { _db = db; _export = export; _logger = logger; }

    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var due = await _db.Set<ReportSchedule>()
            .Where(s => s.IsActive && (s.NextRunAt == null || s.NextRunAt <= now))
            .ToListAsync(ct);

        var processed = 0;
        foreach (var schedule in due)
        {
            try
            {
                var report = await _db.Set<ReportDefinition>()
                    .Where(r => r.Id == schedule.ReportDefinitionId)
                    .Select(r => new { r.NameEn, r.TenantId })
                    .FirstOrDefaultAsync(ct);
                if (report is null) { schedule.IsActive = false; continue; }

                var file = await _export.ExportAsync(schedule.ReportDefinitionId, schedule.ExportFormat, ct);

                var stored = new StoredFile
                {
                    TenantId = report.TenantId,
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    Data = file.Content,
                    SizeBytes = file.Content.LongLength,
                    Category = "ReportSchedule",
                };
                _db.Set<StoredFile>().Add(stored);
                await _db.SaveChangesAsync(ct);

                var link = $"/api/files/{stored.Id}";
                foreach (var email in ScheduleMath.ParseEmails(schedule.Recipients))
                {
                    _db.Set<EmailNotificationQueue>().Add(new EmailNotificationQueue
                    {
                        TenantId = report.TenantId,
                        ToEmail = email,
                        Subject = $"تقرير مجدول: {report.NameEn}",
                        Body = $"تم إنشاء التقرير \"{report.NameEn}\" في {now:yyyy-MM-dd HH:mm} UTC. رابط التنزيل: {link}",
                        Category = "ReportSchedule",
                        EntityId = schedule.ReportDefinitionId,
                        Link = link,
                        Status = EmailQueueStatus.Pending,
                    });
                }

                schedule.LastRunAt = now;
                schedule.NextRunAt = ScheduleMath.ComputeNextRun(schedule.Frequency, now);
                await _db.SaveChangesAsync(ct);
                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Report schedule {ScheduleId} failed.", schedule.Id);
            }
        }
        return processed;
    }
}
```

> Note: this runner reads across tenants (the hosted service runs with no user). `ApplicationDbContext` applies a tenant query filter based on the injected user context; for a background scope the filter may exclude rows. Read `IBackgroundExecutionContext` and the DbContext's tenant-filter setup — if global query filters block cross-tenant reads, either resolve the schedules with `.IgnoreQueryFilters()` here (and set `TenantId` explicitly on every write, which this code already does) or run per-tenant using `IBackgroundExecutionContext`. Prefer `.IgnoreQueryFilters()` on the two read queries (`ReportSchedule`, `ReportDefinition`) since all writes already stamp `TenantId` from `report.TenantId`. Confirm `ReportDefinition` has a `TenantId` property; if it's on a base class, the projection still works.

- [ ] **Step 4: Run the math tests** — `dotnet test ... --filter FullyQualifiedName~ScheduleMathTests` → PASS (6 cases).

- [ ] **Step 5: Add an optional `[SkippableFact]` end-to-end test** in `ReportScheduleRunnerTests.cs` (seed a minimal runnable report + a due schedule, call `RunDueAsync`, assert a `StoredFile` and an `EmailNotificationQueue` row were created and `NextRunAt` advanced). Reuse the seeding shape from `ReportExportServiceTests.cs`. Gate with `Skip.If(string.IsNullOrWhiteSpace(Conn), ...)`.

- [ ] **Step 6: Register in DI** — in `DependencyInjection.cs`:

```csharp
services.AddScoped<HR.Modules.Platform.Services.Reports.IReportScheduleRunner, HR.Modules.Platform.Services.Reports.ReportScheduleRunner>();
```

- [ ] **Step 7: Build + test** — `dotnet build backend/src/HR.Api/HR.Api.csproj` (0 errors); `dotnet test backend/tests/HR.Modules.Platform.Tests` (green).

- [ ] **Step 8: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Reports/IReportScheduleRunner.cs backend/src/HR.Modules/Platform/Services/Reports/ReportScheduleRunner.cs backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs backend/tests/HR.Modules.Platform.Tests/Reports/ReportScheduleRunnerTests.cs
git commit -m "feat(reports): schedule runner (export -> stored file -> email link, NextRunAt roll)"
```

---

## Task B3: `ReportScheduleHostedService` + registration

**Files:**
- Create: `backend/src/HR.Api/Services/ReportScheduleHostedService.cs`
- Modify: `backend/src/HR.Api/Program.cs`

**Interfaces:**
- Consumes: `IReportScheduleRunner.RunDueAsync` (B2), `IServiceScopeFactory`.

- [ ] **Step 1: Create the hosted service** mirroring `DocumentExpiryHostedService`:

```csharp
using HR.Modules.Platform.Services.Reports;

namespace HR.Api.Services;

/// <summary>Runs due report schedules shortly after startup, then hourly. RunDueAsync is idempotent
/// per tick (only schedules whose NextRunAt has passed are picked up), so cadence only affects timeliness.</summary>
public sealed class ReportScheduleHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReportScheduleHostedService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public ReportScheduleHostedService(IServiceScopeFactory scopeFactory, ILogger<ReportScheduleHostedService> logger)
    { _scopeFactory = scopeFactory; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<IReportScheduleRunner>();
                var count = await runner.RunDueAsync(stoppingToken);
                if (count > 0) _logger.LogInformation("Report scheduler processed {Count} schedule(s).", count);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Report scheduler tick failed."); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
```

- [ ] **Step 2: Register in `Program.cs`** next to the existing `AddHostedService<DocumentExpiryHostedService>()` call:

```csharp
builder.Services.AddHostedService<HR.Api.Services.ReportScheduleHostedService>();
```

> Find the existing `AddHostedService<DocumentExpiryHostedService>()` line (grep `DocumentExpiryHostedService` in `Program.cs`) and add this immediately after it.

- [ ] **Step 3: Build** — `dotnet build backend/src/HR.Api/HR.Api.csproj` → 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/HR.Api/Services/ReportScheduleHostedService.cs backend/src/HR.Api/Program.cs
git commit -m "feat(reports): hourly hosted service driving the schedule runner"
```

---

## Task B4: Schedule UI (frontend)

**Files:**
- Create: `src/components/reports/schedule-panel.tsx`
- Modify: `src/app/(dashboard)/reports/[id]/page.tsx` (mount the panel)

**Interfaces:**
- Consumes: `getSchedules`, `addSchedule`, `deleteSchedule`, `ReportSchedule` from A1.

- [ ] **Step 1: Create `schedule-panel.tsx`:**

```tsx
"use client";

import { useCallback, useEffect, useState } from "react";
import { Trash2 } from "lucide-react";
import { toast } from "sonner";
import { getSchedules, addSchedule, deleteSchedule, ReportSchedule } from "@/lib/api/reports";

const FREQ = [{ v: 1, l: "يومي" }, { v: 2, l: "أسبوعي" }, { v: 3, l: "شهري" }, { v: 4, l: "ربع سنوي" }];
const FMT = [{ v: 1, l: "Excel" }, { v: 2, l: "CSV" }, { v: 5, l: "PDF" }];

export function SchedulePanel({ reportId }: { reportId: string }) {
  const [items, setItems] = useState<ReportSchedule[]>([]);
  const [freq, setFreq] = useState(1); const [fmt, setFmt] = useState(1); const [emails, setEmails] = useState("");

  const load = useCallback(async () => {
    try { setItems(await getSchedules(reportId)); } catch { /* ignore */ }
  }, [reportId]);
  useEffect(() => { queueMicrotask(() => { load(); }); }, [load]);

  const add = async () => {
    const list = emails.split(",").map((e) => e.trim()).filter(Boolean);
    if (list.length === 0) { toast.error("أدخل بريدًا واحدًا على الأقل"); return; }
    try {
      await addSchedule(reportId, { frequency: freq, exportFormat: fmt, recipients: JSON.stringify(list) });
      setEmails(""); await load(); toast.success("تمت إضافة الجدولة");
    } catch { toast.error("تعذّر إضافة الجدولة"); }
  };

  return (
    <div className="border border-border bg-card p-4 space-y-3">
      <h3 className="font-semibold">الجدولة والتسليم</h3>
      <ul className="space-y-1 text-sm">
        {items.map((s) => (
          <li key={s.id} className="flex items-center justify-between">
            <span>{FREQ.find((f) => String(f.v) === s.frequency || f.l === s.frequency)?.l ?? s.frequency} · {s.exportFormat} · {s.nextRunAt ? new Date(s.nextRunAt).toLocaleDateString() : "—"}</span>
            <button className="text-destructive" onClick={async () => { await deleteSchedule(s.id); await load(); }}><Trash2 className="h-4 w-4" /></button>
          </li>
        ))}
        {items.length === 0 && <li className="text-muted-foreground">لا توجد جدولة.</li>}
      </ul>
      <div className="flex flex-wrap items-center gap-2">
        <select value={freq} onChange={(e) => setFreq(Number(e.target.value))} className="h-9 border border-border bg-background px-2 text-sm">
          {FREQ.map((f) => <option key={f.v} value={f.v}>{f.l}</option>)}
        </select>
        <select value={fmt} onChange={(e) => setFmt(Number(e.target.value))} className="h-9 border border-border bg-background px-2 text-sm">
          {FMT.map((f) => <option key={f.v} value={f.v}>{f.l}</option>)}
        </select>
        <input value={emails} onChange={(e) => setEmails(e.target.value)} placeholder="بريد1، بريد2" className="h-9 flex-1 border border-border bg-background px-3 text-sm" />
        <button onClick={add} className="inline-flex h-9 items-center bg-primary px-4 text-sm text-primary-foreground">إضافة</button>
      </div>
    </div>
  );
}
```

> Note: `ReportScheduleDto.frequency`/`exportFormat` serialize as enum **names or numbers** depending on the API's JSON options. The label lookups above tolerate both. Verify against a live response and simplify if the API returns names (e.g. `"Daily"`).

- [ ] **Step 2: Mount it in the viewer** — in `reports/[id]/page.tsx`, import and render `<SchedulePanel reportId={id} />` below the table (guard with `canEdit`).

- [ ] **Step 3: Verify the build** — `npx next build` → 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/components/reports/schedule-panel.tsx "src/app/(dashboard)/reports/[id]/page.tsx"
git commit -m "feat(reports): schedule panel UI (list/add/delete) in the viewer"
```

---

# SUB-PROJECT C — WPS/SIF report export (backend + button)

## Task C1: `SifReportExporter` — map report rows → WPS SIF bytes

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/Reports/SifReportExporter.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/SifReportExporterTests.cs`

**Interfaces:**
- Consumes: `ReportResult`, `ReportColumn`, `ReportRow` (`ReportModels.cs`); `BankPaymentRow`, `SaudiWpsSifProfile`, `SaudiWpsSifValidator`, `BankFieldMapper`, `CsvExportWriter` (all in `HR.Application.Engines.Finance.Export[.Bank]`); `ValidationException` (`HR.Application.Common.Exceptions`).
- Produces: `static class SifReportExporter { byte[] Export(ReportResult result); }`. Required WPS column codes (case-insensitive): `EmployeeNumber, NationalId, EmployeeName, Iban, BankCode, NetAmount, Currency`. Missing any → `ValidationException` naming them.

- [ ] **Step 1: Write DB-free unit tests** in `SifReportExporterTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using HR.Application.Common.Exceptions;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class SifReportExporterTests
{
    private static ReportColumn Col(string code) => new() { Code = code, Label = code };

    private static ReportResult ValidResult()
    {
        var result = new ReportResult
        {
            Columns = new()
            {
                Col("EmployeeNumber"), Col("NationalId"), Col("EmployeeName"),
                Col("Iban"), Col("BankCode"), Col("NetAmount"), Col("Currency"),
            },
            Rows = new()
            {
                new ReportRow(new Dictionary<string, object?>
                {
                    ["EmployeeNumber"] = "E1", ["NationalId"] = "1122334455", ["EmployeeName"] = "Ali",
                    ["Iban"] = "SA0380000000608010167519", ["BankCode"] = "RIBLSARI",
                    ["NetAmount"] = 5000.0, ["Currency"] = "SAR",
                }),
            },
        };
        return result;
    }

    [Fact]
    public void Exports_valid_sif_csv_with_header_and_row()
    {
        var bytes = SifReportExporter.Export(ValidResult());
        bytes.Should().NotBeNullOrEmpty();
        var text = Encoding.UTF8.GetString(bytes);
        text.Should().Contain("IBAN");
        text.Should().Contain("SA0380000000608010167519");
        text.Should().Contain("5000.00"); // WPS 2-decimal formatting
    }

    [Fact]
    public void Missing_required_column_throws_naming_it()
    {
        var result = ValidResult();
        result.Columns.RemoveAll(c => c.Code == "Iban");
        var act = () => SifReportExporter.Export(result);
        act.Should().Throw<ValidationException>().Which.Message.Should().Contain("Iban");
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~SifReportExporterTests` → FAIL (type missing).

- [ ] **Step 3: Implement** `SifReportExporter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Finance.Export;
using HR.Application.Engines.Finance.Export.Bank;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Projects a report's rows through the existing Saudi WPS/SIF bank profile. The report must
/// expose the canonical WPS column codes; otherwise a ValidationException (400) names what is missing.</summary>
public static class SifReportExporter
{
    private static readonly string[] Required =
        { "EmployeeNumber", "NationalId", "EmployeeName", "Iban", "BankCode", "NetAmount", "Currency" };

    public static byte[] Export(ReportResult result)
    {
        var present = result.Columns.Select(c => c.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = Required.Where(r => !present.Contains(r)).ToList();
        if (missing.Count > 0)
            throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure(
                "columns", $"Report is missing WPS columns: {string.Join(", ", missing)}.") });

        var dataRows = CollectRows(result);
        var payments = dataRows.Select(ToPayment).ToList();

        var profile = new SaudiWpsSifProfile();
        var errors = new SaudiWpsSifValidator().Validate(payments);
        if (errors.Count > 0)
            throw new ValidationException(errors.Select(e =>
                new FluentValidation.Results.ValidationFailure(e.Field, $"{e.EmployeeNumber}: {e.Message}")).ToArray());

        var dataset = BankFieldMapper.Map(payments, profile);
        return new CsvExportWriter().Write(dataset);
    }

    private static IEnumerable<ReportRow> CollectRows(ReportResult result)
    {
        if (result.Rows.Count > 0) return result.Rows;
        var acc = new List<ReportRow>();
        void Walk(IEnumerable<ReportGroup> groups)
        {
            foreach (var g in groups)
            {
                if (g.SubGroups.Count > 0) Walk(g.SubGroups);
                else acc.AddRange(g.Rows);
            }
        }
        Walk(result.Groups);
        return acc;
    }

    private static BankPaymentRow ToPayment(ReportRow row)
    {
        string S(string code) => row.TryGetValue(code, out var v) ? v?.ToString() ?? "" : "";
        decimal D(string code) => row.TryGetValue(code, out var v) && v is not null
            ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : 0m;
        return new BankPaymentRow(
            EmployeeNumber: S("EmployeeNumber"),
            EmployeeName: S("EmployeeName"),
            Iban: S("Iban"),
            BankCode: S("BankCode"),
            NationalId: S("NationalId"),
            NetAmount: D("NetAmount"),
            Currency: S("Currency"));
    }
}
```

> Note: confirm `CsvExportWriter` has a parameterless ctor and lives in `HR.Application.Engines.Finance.Export`; also confirm `SaudiWpsSifValidator` has a parameterless ctor and `Validate` returns `IReadOnlyList<BankValidationError>` (verified in `BankPipeline.cs`). If the validator rejects the sample IBAN in the test, adjust the test's IBAN/bank code to a validator-accepted value (open `SaudiWpsSifValidator.cs` for the rules) — keep the two assertions (valid → bytes; missing column → throw).

- [ ] **Step 4: Run to verify it passes** — same filter → PASS (2 cases). If the validator is strict, fix the test fixture per the note.

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Reports/SifReportExporter.cs backend/tests/HR.Modules.Platform.Tests/Reports/SifReportExporterTests.cs
git commit -m "feat(reports): WPS/SIF report exporter via existing bank profile pipeline"
```

---

## Task C2: Wire `format=sif` into the export service + query

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Services/Reports/IReportExportService.cs`
- Modify: `backend/src/HR.Modules/Platform/Services/Reports/ReportExportService.cs`
- Modify: `backend/src/HR.Modules/Platform/Queries/Reports/ReportExportQueries.cs`

**Interfaces:**
- Consumes: `SifReportExporter.Export` (C1), `IReportExecutionService.RunForExportAsync`, `IReportAccessService.EnsureCanReadAsync`.
- Produces: `Task<ReportExportFile> ExportSifAsync(Guid reportId, CancellationToken ct)` on `IReportExportService`; `ExportReportQuery` routes `format == "sif"` to it.

- [ ] **Step 1: Add the interface method** to `IReportExportService.cs`:

```csharp
Task<ReportExportFile> ExportSifAsync(Guid reportId, CancellationToken ct);
```

- [ ] **Step 2: Implement it** in `ReportExportService.cs` (reuse the access gate + `RunForExportAsync` + the report metadata lookup already present in `ExportAsync`):

```csharp
public async Task<ReportExportFile> ExportSifAsync(Guid reportId, CancellationToken ct)
{
    await _access.EnsureCanReadAsync(reportId, ct);

    var meta = await _db.Set<HR.Domain.Engines.Reports.ReportDefinition>()
        .Where(r => r.Id == reportId).Select(r => new { r.Code }).FirstOrDefaultAsync(ct)
        ?? throw new HR.Application.Common.Exceptions.NotFoundException("ReportDefinition", reportId);

    var result = await _exec.RunForExportAsync(reportId, ct);
    var bytes = SifReportExporter.Export(result); // throws ValidationException if WPS columns are missing

    var stamp = DateTime.UtcNow.ToString("yyyyMMdd");
    var safe = string.IsNullOrWhiteSpace(meta.Code) ? "report" : meta.Code;
    return new ReportExportFile(bytes, "text/csv", $"{safe}-wps-sif-{stamp}.csv");
}
```

> Match the field names/usings already in `ReportExportService.cs` (`_access`, `_exec`, `_db`). If `Microsoft.EntityFrameworkCore` / the exceptions namespace aren't imported, add them (they already are, since `ExportAsync` uses the same lookups).

- [ ] **Step 3: Route the format** in `ReportExportQueries.cs` `ExportReportQueryHandler.Handle` — before the `Enum.TryParse`, special-case `sif`:

```csharp
public Task<ReportExportFile> Handle(ExportReportQuery request, CancellationToken ct)
{
    if (string.Equals(request.Format, "sif", StringComparison.OrdinalIgnoreCase))
        return _export.ExportSifAsync(request.Id, ct);

    if (!Enum.TryParse<ExportFormat>(request.Format, ignoreCase: true, out var fmt))
        throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("format", $"Unknown export format '{request.Format}'. Use excel, csv, pdf, or sif.") });
    return _export.ExportAsync(request.Id, fmt, ct);
}
```

- [ ] **Step 4: Build + test** — `dotnet build backend/src/HR.Api/HR.Api.csproj` (0 errors); `dotnet test backend/tests/HR.Modules.Platform.Tests` (green).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Reports/IReportExportService.cs backend/src/HR.Modules/Platform/Services/Reports/ReportExportService.cs backend/src/HR.Modules/Platform/Queries/Reports/ReportExportQueries.cs
git commit -m "feat(reports): route export format=sif to the WPS/SIF exporter"
```

---

## Task C3: WPS/SIF export button (frontend)

**Files:**
- Modify: `src/lib/api/reports.ts` (add `"sif"` to `ExportFormat` + extension map)
- Modify: `src/app/(dashboard)/reports/page.tsx` and `src/app/(dashboard)/reports/[id]/page.tsx` (add the button)

- [ ] **Step 1:** In `reports.ts`, widen the type and extension map:

```typescript
export type ExportFormat = "excel" | "csv" | "pdf" | "sif";
```
and in `EXT`:
```typescript
const EXT: Record<ExportFormat, string> = { excel: "xlsx", csv: "csv", pdf: "pdf", sif: "csv" };
```

- [ ] **Step 2:** Add a WPS/SIF entry to the `FORMATS` array in **both** the list page and the viewer page:

```tsx
{ key: "sif", label: "WPS/SIF", icon: FileText },  // list page (icon import already present)
{ key: "sif", label: "WPS/SIF" },                   // viewer page
```

The existing `exportReport` already surfaces a 400 as a toast; on a report missing WPS columns the user sees the failure. Optionally improve the message: in `exportReport`, when `res.status === 400`, read the JSON body and toast its message instead of the generic text.

- [ ] **Step 3: Verify the build** — `npx next build` → 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/lib/api/reports.ts "src/app/(dashboard)/reports/page.tsx" "src/app/(dashboard)/reports/[id]/page.tsx"
git commit -m "feat(reports): WPS/SIF export button (list + viewer)"
```

---

# Final verification & deploy

- [ ] `dotnet build backend/src/HR.Api/HR.Api.csproj` → 0 errors.
- [ ] `dotnet test backend/tests/HR.Modules.Platform.Tests` → all green (DB-touching skipped locally).
- [ ] `npx next build` → 0 errors.
- [ ] **Deploy backend once** (after B + C): `dotnet publish backend/src/HR.Api -c Release -o ./publish`, zip with forward-slash entries (Python `zipfile`), `az webapp deploy --resource-group HR --name hrcloud-api-v4xd --src-path <zip> --type zip`. Verify `GET /api/platform/reports/{id}/schedules` and `.../export?format=sif` appear in the OpenAPI spec and return 401 unauthenticated.
- [ ] **Push to `main`** → Vercel auto-deploys the frontend (builder, viewer, schedule panel, SIF button).
- [ ] Update memory `reports-engine-r1.md`: 3b builder/viewer, R4 scheduling, R3 SIF all shipped.

## Self-review notes (author)
- **Spec coverage:** A (A1 client, A2 viewer+table, A3 wizard, A4 list actions) ✓; B (B1 list query, B2 runner+math, B3 hosted service, B4 UI) ✓; C (C1 exporter, C2 wiring, C3 button) ✓. Truncated warning (A2), grouped subtotals/grand total (A2 table), no-migration (all), reuse of bank pipeline (C1) and email queue + stored file (B2) all covered.
- **No migration** confirmed — every table/column used already exists.
- **Type consistency:** `ReportExportFile`, `ExportFormat` enum values, `ReportScheduleFrequency`, catalog DTO field names, and `BankPaymentRow` ctor args all copied from source. FE `ExportFormat` union widened to include `"sif"` in C3 (used by A2/A4 buttons).
- **Known unknowns flagged inline** (not placeholders): Next.js params-shape, `apiFetch` envelope handling, tenant query-filter behavior in background scope, enum JSON serialization (names vs numbers), validator strictness — each has a concrete "confirm X, else do Y" instruction.
