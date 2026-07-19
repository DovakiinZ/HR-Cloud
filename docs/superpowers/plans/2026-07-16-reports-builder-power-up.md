# Reports Builder/Viewer Power-Up (SP-1a) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface the already-built backend (joins, computed/formula fields, runtime parameters) in the report builder + viewer, and fix the builder's object-registry permission gap.

**Architecture:** Frontend-only against the canonical `src/lib/api/reports.ts` client, plus one backend `[RequirePermission]` edit. The builder gains a Joins step, joined-object + computed fields in the Fields step, and a runtime-parameter toggle on filters; the viewer gains a parameters panel that feeds `runReport`/`exportReport`. No DB migration.

**Tech Stack:** .NET 8 (one attribute edit), Next.js 16.2.6 App Router + TypeScript (RTL, Thamania tokens), existing canonical reports client + `ReportTable`.

## Global Constraints

- **No DB migration.** All backend (relationships, `validateFormula`, `calculationText`, parameter binding) already exists and is deployed.
- **Reuse the canonical client** (`src/lib/api/reports.ts`) — it already exports `getReportRelationships`, `addReportRelationship`, `deleteReportRelationship`, `validateFormula`, `addReportField` (with `calculationText`), `runReport(id, {page,pageSize,parameters})`, `exportReport(id, format, fallbackName, parameters)`. Do NOT modify the client.
- **Enum & type facts:** `JoinType = "Inner"|"Left"|"Right"` (string on the command — binds directly). `ReportType/ReportScope/ReportFieldType/AggregationType/ReportFilterOperator/SortDirection` are string unions; report command enums now bind names (`main@f304f2c`). `SelectableObject = { id /*ObjectDefinition Guid*/, code, nameAr, nameEn, module, catalog: { fields: CatalogField[] } }`; `CatalogField = { code, nameEn, nameAr, fieldType, isMeasure, isGroupable, isFilterable, isDate, isReference, referenceObjectCode?, options? }`. `ReportRelationship = { id, reportDefinitionId, sourceObjectId, targetObjectId, joinField, joinType, sortOrder }`. `ReportField` carries `fieldType`, `calculationText?`, `aggregation?`.
- **Runtime parameter convention:** a filter with `isParameter=true` is prompt-on-run; parameters pass as `{ [fieldCode]: value }` and, for a `Between` filter, the upper bound key is `` `${fieldCode}:to` `` (matches backend `ReportParameterBinder.UpperBoundSuffix`). Omit blank values (server falls back to the filter's stored default).
- **`apiFetch` stringifies bodies internally** — pass plain objects.
- **FE gate:** `npx next build` compiles with 0 errors (no FE test runner). **BE gate:** `dotnet build backend/src/HR.Api/HR.Api.csproj` = 0 errors. Commit after each task.
- **Deploy** (after all tasks): zip-deploy API once (perm change), push → Vercel auto-deploys FE.

---

## Task 1: Backend — allow `Platform.Reports.View` on the object registry read endpoints

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Controllers/ObjectRegistryController.cs`

**Interfaces:**
- Produces: `GET /api/platform/objects` and `GET /api/platform/objects/{code}` accept callers holding EITHER `Platform.Objects.View` OR `Platform.Reports.View` (the attribute is OR across listed permissions, matching `ObjectCatalogController`). Write endpoints are unchanged.

- [ ] **Step 1: Edit the two GET endpoints.** In `ObjectRegistryController.cs`, change ONLY the `[RequirePermission(...)]` on `GetAll` (the `[HttpGet]` with no route) and `GetByCode` (`[HttpGet("{code}")]`):

```csharp
    [HttpGet]
    [RequirePermission("Platform.Objects.View", "Platform.Reports.View")]
    public async Task<ActionResult<ApiResponse<List<ObjectDefinitionDto>>>> GetAll(CancellationToken ct)
```
```csharp
    [HttpGet("{code}")]
    [RequirePermission("Platform.Objects.View", "Platform.Reports.View")]
    public async Task<ActionResult<ApiResponse<ObjectDefinitionDto>>> GetByCode(string code, CancellationToken ct)
```
Leave every other endpoint (Create/Update/Delete/fields/relationships/permissions) exactly as-is (still `Platform.Objects.*`).

- [ ] **Step 2: Build.** Run: `dotnet build backend/src/HR.Api/HR.Api.csproj`
Expected: `Build succeeded. 0 Error(s)`. (No new test — this is a declarative auth-attribute widening mirroring the existing `ObjectCatalogController` pattern; the OR semantics are already exercised there.)

- [ ] **Step 3: Commit**

```bash
git add backend/src/HR.Modules/Platform/Controllers/ObjectRegistryController.cs
git commit -m "fix(reports): allow Platform.Reports.View on object registry read endpoints (builder access)"
```

---

## Task 2: Builder — Joins step + joined-object fields + computed fields + parameter toggle

**Files:**
- Modify (full replace): `src/app/(dashboard)/reports/builder/[[...id]]/page.tsx`

**Interfaces:**
- Consumes: canonical client (`getReportRelationships`, `addReportRelationship`, `deleteReportRelationship`, `validateFormula`, plus existing report/field/filter/grouping/sorting calls), `ReportTable`.
- Produces: a 6-step wizard (Basics → Joins → Fields → Filters → Grouping+Sorting → Preview); Fields step can pull from any object in the report and add computed fields; Filters can be marked runtime parameters.

- [ ] **Step 1: Replace the entire file** `src/app/(dashboard)/reports/builder/[[...id]]/page.tsx` with:

```tsx
"use client";

import { use, useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { Loader2, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import {
  getReport, createReport, updateReport, publishReport, runReport,
  addReportField, deleteReportField, addReportFilter, deleteReportFilter,
  addReportGrouping, deleteReportGrouping, addReportSorting, deleteReportSorting,
  addReportRelationship, deleteReportRelationship, validateFormula,
  getSelectableObjects,
  ReportDefinition, ReportResult, SelectableObject, CatalogField,
  ReportType, ReportScope, ReportFilterOperator, JoinType,
} from "@/lib/api/reports";
import { ReportTable } from "@/components/reports/report-table";

const CLS_INPUT = "h-9 w-full border border-border bg-background px-3 text-sm";
const CLS_BTN_PRIMARY = "inline-flex h-9 items-center gap-2 bg-primary px-4 text-sm text-primary-foreground disabled:opacity-50";

const REPORT_TYPES: { v: ReportType; l: string }[] = [
  { v: "Tabular", l: "جدولي" }, { v: "Summary", l: "ملخّص" }, { v: "Matrix", l: "مصفوفة" }, { v: "Chart", l: "مخطط" },
];
const SCOPES: { v: ReportScope; l: string }[] = [
  { v: "Personal", l: "شخصي" }, { v: "Company", l: "الشركة" }, { v: "Department", l: "قسم" }, { v: "Shared", l: "مشترك" },
];
const OPERATORS: { v: ReportFilterOperator; l: string }[] = [
  { v: "Equals", l: "يساوي" }, { v: "NotEquals", l: "لا يساوي" }, { v: "Contains", l: "يحتوي" },
  { v: "StartsWith", l: "يبدأ بـ" }, { v: "EndsWith", l: "ينتهي بـ" }, { v: "GreaterThan", l: "أكبر من" },
  { v: "LessThan", l: "أصغر من" }, { v: "Between", l: "بين" },
];
const JOIN_TYPES: { v: JoinType; l: string }[] = [
  { v: "Inner", l: "داخلي (Inner)" }, { v: "Left", l: "يسار (Left)" }, { v: "Right", l: "يمين (Right)" },
];

export default function ReportBuilderPage({ params }: { params: Promise<{ id?: string[] }> }) {
  const { id: idParam } = use(params);
  const router = useRouter();
  const existingId = idParam?.[0];

  const [step, setStep] = useState(0);
  const [reportId, setReportId] = useState<string | undefined>(existingId);
  const [report, setReport] = useState<ReportDefinition | null>(null);
  const [selectableObjects, setSelectableObjects] = useState<SelectableObject[]>([]);
  const [saving, setSaving] = useState(false);
  const [preview, setPreview] = useState<ReportResult | null>(null);

  const [form, setForm] = useState<{
    code: string; nameEn: string; nameAr: string; description: string;
    reportType: ReportType; scope: ReportScope; primaryObjectId: string;
  }>({ code: "", nameEn: "", nameAr: "", description: "", reportType: "Tabular", scope: "Company", primaryObjectId: "" });

  useEffect(() => { queueMicrotask(async () => {
    try { setSelectableObjects(await getSelectableObjects()); }
    catch { toast.error("تعذر تحميل الكائنات"); }
    if (existingId) {
      try {
        const r = await getReport(existingId);
        setReport(r);
        setForm({ code: r.code, nameEn: r.nameEn, nameAr: r.nameAr, description: r.description ?? "", reportType: r.reportType, scope: r.scope, primaryObjectId: r.primaryObjectId });
      } catch { toast.error("تعذر تحميل التقرير"); }
    }
  }); }, [existingId]);

  const objMap = useMemo(() => new Map(selectableObjects.map((o) => [o.id, o])), [selectableObjects]);
  const primaryObjectId = report?.primaryObjectId ?? form.primaryObjectId;

  // Objects present in the report = primary first, then each relationship target (in join order).
  const reportObjects = useMemo(() => {
    const ids = [primaryObjectId, ...((report?.relationships ?? []).slice().sort((a, b) => a.sortOrder - b.sortOrder).map((r) => r.targetObjectId))];
    return ids.map((id) => objMap.get(id)).filter((o): o is SelectableObject => !!o);
  }, [objMap, primaryObjectId, report?.relationships]);

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
    try { setPreview(await runReport(reportId, { page: 1, pageSize: 50 })); }
    catch { toast.error("تعذّر تشغيل المعاينة"); }
  };

  const steps = ["الأساسيات", "الروابط", "الحقول", "عوامل التصفية", "التجميع والفرز", "المعاينة"];

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
          <Field label="الكود"><input disabled={!!reportId} value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} className={CLS_INPUT} /></Field>
          <Field label="الاسم (عربي)"><input value={form.nameAr} onChange={(e) => setForm({ ...form, nameAr: e.target.value })} className={CLS_INPUT} /></Field>
          <Field label="الاسم (إنجليزي)"><input value={form.nameEn} onChange={(e) => setForm({ ...form, nameEn: e.target.value })} className={CLS_INPUT} /></Field>
          <Field label="الوصف"><input value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} className={CLS_INPUT} /></Field>
          <Field label="النوع">
            <select value={form.reportType} onChange={(e) => setForm({ ...form, reportType: e.target.value as ReportType })} className={CLS_INPUT}>
              {REPORT_TYPES.map((t) => <option key={t.v} value={t.v}>{t.l}</option>)}
            </select>
          </Field>
          <Field label="النطاق">
            <select value={form.scope} onChange={(e) => setForm({ ...form, scope: e.target.value as ReportScope })} className={CLS_INPUT}>
              {SCOPES.map((t) => <option key={t.v} value={t.v}>{t.l}</option>)}
            </select>
          </Field>
          {!reportId && (
            <Field label="الكائن الأساسي">
              <select value={form.primaryObjectId} onChange={(e) => setForm({ ...form, primaryObjectId: e.target.value })} className={CLS_INPUT}>
                <option value="">— اختر —</option>
                {selectableObjects.map((o) => <option key={o.id} value={o.id}>{o.nameAr || o.nameEn}</option>)}
              </select>
              <p className="text-xs text-muted-foreground mt-1">ملاحظة: الكائن الأساسي يُحدَّد مرة واحدة عند الإنشاء.</p>
            </Field>
          )}
          <button onClick={saveBasics} disabled={saving || (!reportId && (!form.code || !form.primaryObjectId))} className={CLS_BTN_PRIMARY}>
            {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : null} حفظ ومتابعة
          </button>
        </div>
      )}

      {/* Step 1 — Joins */}
      {step === 1 && reportId && (
        <JoinsStep reportId={reportId} report={report} selectableObjects={selectableObjects} reportObjects={reportObjects} objMap={objMap} onChange={refreshReport} />
      )}

      {/* Step 2 — Fields */}
      {step === 2 && reportId && (
        <FieldsStep report={report} reportObjects={reportObjects} primaryObjectId={primaryObjectId} reportId={reportId} onChange={refreshReport} />
      )}

      {/* Step 3 — Filters */}
      {step === 3 && reportId && (
        <ChildList
          title="عوامل التصفية"
          items={(report?.filters ?? []).map((f) => ({ id: f.id, label: `${f.fieldCode} ${f.operator} ${f.value ?? ""}${f.isParameter ? " · معامل" : ""}` }))}
          onDelete={async (fid) => { await deleteReportFilter(fid); await refreshReport(); }}
          adder={<FilterAdder fields={report?.fields ?? []} onAdd={async (b) => { await addReportFilter(reportId, b); await refreshReport(); }} />}
        />
      )}

      {/* Step 4 — Grouping + Sorting */}
      {step === 4 && reportId && (
        <div className="grid gap-6 md:grid-cols-2">
          <ChildList
            title="التجميع"
            items={(report?.groupings ?? []).map((g) => ({ id: g.id, label: g.fieldCode }))}
            onDelete={async (gid) => { await deleteReportGrouping(gid); await refreshReport(); }}
            adder={<CodePicker fields={report?.fields ?? []} label="أضف تجميعًا" onPick={async (code) => { await addReportGrouping(reportId, { fieldCode: code, sortOrder: 0 }); await refreshReport(); }} />}
          />
          <ChildList
            title="الفرز"
            items={(report?.sortings ?? []).map((s) => ({ id: s.id, label: `${s.fieldCode} · ${s.direction}` }))}
            onDelete={async (sid) => { await deleteReportSorting(sid); await refreshReport(); }}
            adder={<CodePicker fields={report?.fields ?? []} label="أضف فرزًا" onPick={async (code) => { await addReportSorting(reportId, { fieldCode: code, direction: "Ascending", sortOrder: 0 }); await refreshReport(); }} />}
          />
        </div>
      )}

      {/* Step 5 — Preview */}
      {step === 5 && reportId && (
        <div className="space-y-4">
          <div className="flex items-center gap-2">
            <button onClick={runPreview} className={CLS_BTN_PRIMARY}>تشغيل المعاينة</button>
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

function JoinsStep({ reportId, report, selectableObjects, reportObjects, objMap, onChange }: {
  reportId: string; report: ReportDefinition | null; selectableObjects: SelectableObject[];
  reportObjects: SelectableObject[]; objMap: Map<string, SelectableObject>; onChange: () => Promise<void>;
}) {
  const [sourceId, setSourceId] = useState("");
  const [targetId, setTargetId] = useState("");
  const [joinField, setJoinField] = useState("");
  const [joinType, setJoinType] = useState<JoinType>("Inner");
  const [busy, setBusy] = useState(false);

  const inReport = new Set(reportObjects.map((o) => o.id));
  const targets = selectableObjects.filter((o) => !inReport.has(o.id));
  const sourceObj = objMap.get(sourceId) ?? reportObjects[0];
  const sourceFields = sourceObj?.catalog.fields ?? [];

  const name = (id: string) => objMap.get(id)?.nameAr || objMap.get(id)?.nameEn || id.slice(0, 8);

  const add = async () => {
    const src = sourceId || reportObjects[0]?.id;
    if (!src || !targetId || !joinField) { toast.error("أكمل حقول الربط"); return; }
    setBusy(true);
    try {
      await addReportRelationship(reportId, { sourceObjectId: src, targetObjectId: targetId, joinField, joinType, sortOrder: report?.relationships?.length ?? 0 });
      setTargetId(""); setJoinField("");
      await onChange();
      toast.success("تمت إضافة الربط");
    } catch { /* addReportRelationship surfaces backend validation via apiFetch toast */ }
    finally { setBusy(false); }
  };

  return (
    <div className="border border-border bg-card p-4 space-y-4 max-w-3xl">
      <h3 className="font-semibold">روابط الكائنات (Joins)</h3>
      <ul className="space-y-1 text-sm">
        {(report?.relationships ?? []).slice().sort((a, b) => a.sortOrder - b.sortOrder).map((r) => (
          <li key={r.id} className="flex items-center justify-between">
            <span>{name(r.sourceObjectId)} → {name(r.targetObjectId)} · {r.joinField} · {r.joinType}</span>
            <button className="text-destructive" onClick={async () => { await deleteReportRelationship(r.id); await onChange(); }}><Trash2 className="h-4 w-4" /></button>
          </li>
        ))}
        {(report?.relationships?.length ?? 0) === 0 && <li className="text-muted-foreground">لا توجد روابط — التقرير على كائن واحد.</li>}
      </ul>

      <div className="grid gap-2 md:grid-cols-2">
        <label className="block text-sm">المصدر
          <select value={sourceId} onChange={(e) => { setSourceId(e.target.value); setJoinField(""); }} className={CLS_INPUT}>
            {reportObjects.map((o) => <option key={o.id} value={o.id}>{o.nameAr || o.nameEn}</option>)}
          </select>
        </label>
        <label className="block text-sm">الكائن المرتبط
          <select value={targetId} onChange={(e) => setTargetId(e.target.value)} className={CLS_INPUT}>
            <option value="">— اختر —</option>
            {targets.map((o) => <option key={o.id} value={o.id}>{o.nameAr || o.nameEn}</option>)}
          </select>
        </label>
        <label className="block text-sm">حقل الربط (على المصدر)
          <select value={joinField} onChange={(e) => setJoinField(e.target.value)} className={CLS_INPUT}>
            <option value="">— حقل —</option>
            {sourceFields.map((f) => <option key={f.code} value={f.code}>{f.nameAr || f.nameEn} ({f.code})</option>)}
          </select>
        </label>
        <label className="block text-sm">نوع الربط
          <select value={joinType} onChange={(e) => setJoinType(e.target.value as JoinType)} className={CLS_INPUT}>
            {JOIN_TYPES.map((t) => <option key={t.v} value={t.v}>{t.l}</option>)}
          </select>
        </label>
      </div>
      <button onClick={add} disabled={busy} className={CLS_BTN_PRIMARY}>{busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />} أضف رابطًا</button>
    </div>
  );
}

function FieldsStep({ report, reportObjects, primaryObjectId, reportId, onChange }: {
  report: ReportDefinition | null; reportObjects: SelectableObject[]; primaryObjectId: string; reportId: string; onChange: () => Promise<void>;
}) {
  const [scopeId, setScopeId] = useState(primaryObjectId);
  const scopeObj = reportObjects.find((o) => o.id === scopeId) ?? reportObjects[0];
  const availableFields: CatalogField[] = scopeObj?.catalog.fields ?? [];

  const addField = async (f: CatalogField) => {
    const isMeasure = f.isMeasure;
    await addReportField(reportId, {
      fieldType: isMeasure ? "AggregateField" : "ObjectField",
      objectDefinitionId: scopeObj && scopeObj.id !== primaryObjectId ? scopeObj.id : null,
      fieldCode: f.code, displayNameEn: f.nameEn, displayNameAr: f.nameAr,
      aggregation: isMeasure ? "Sum" : null, width: 120, sortOrder: report?.fields.length ?? 0,
    });
    await onChange();
  };

  return (
    <div className="space-y-6">
      <div className="grid gap-6 md:grid-cols-2">
        <div className="border border-border bg-card p-4">
          <div className="flex items-center justify-between mb-3 gap-2">
            <h3 className="font-semibold">الحقول المتاحة</h3>
            {reportObjects.length > 1 && (
              <select value={scopeId} onChange={(e) => setScopeId(e.target.value)} className="h-8 border border-border bg-background px-2 text-xs">
                {reportObjects.map((o) => <option key={o.id} value={o.id}>{o.nameAr || o.nameEn}</option>)}
              </select>
            )}
          </div>
          {availableFields.length === 0 && <p className="text-sm text-muted-foreground">لا توجد حقول محمّلة.</p>}
          <ul className="space-y-1 max-h-96 overflow-auto">
            {availableFields.map((f) => (
              <li key={f.code} className="flex items-center justify-between">
                <span className="text-sm">{f.nameAr || f.nameEn}</span>
                <button className="text-primary" title="أضف" onClick={() => addField(f)}><Plus className="h-4 w-4" /></button>
              </li>
            ))}
          </ul>
        </div>
        <div className="border border-border bg-card p-4">
          <h3 className="font-semibold mb-3">الحقول المختارة</h3>
          <ul className="space-y-1">
            {(report?.fields ?? []).map((f) => (
              <li key={f.id} className="flex items-center justify-between">
                <span className="text-sm">{f.fieldType === "CalculatedField" ? "ƒ " : ""}{f.displayNameAr || f.displayNameEn}{f.aggregation ? ` (${f.aggregation})` : ""}</span>
                <button className="text-destructive" onClick={async () => { await deleteReportField(f.id); await onChange(); }}><Trash2 className="h-4 w-4" /></button>
              </li>
            ))}
          </ul>
        </div>
      </div>
      <ComputedFieldForm reportId={reportId} sortOrder={report?.fields.length ?? 0} onAdded={onChange} />
    </div>
  );
}

function ComputedFieldForm({ reportId, sortOrder, onAdded }: { reportId: string; sortOrder: number; onAdded: () => Promise<void> }) {
  const [open, setOpen] = useState(false);
  const [nameAr, setNameAr] = useState(""); const [nameEn, setNameEn] = useState("");
  const [formula, setFormula] = useState(""); const [format, setFormat] = useState("");
  const [valid, setValid] = useState<{ isValid: boolean; error?: string | null } | null>(null);
  const [busy, setBusy] = useState(false);

  // Debounced live validation as the formula is typed.
  useEffect(() => {
    if (!formula.trim()) { setValid(null); return; }
    const t = setTimeout(async () => {
      try { setValid(await validateFormula(formula)); }
      catch { setValid({ isValid: false, error: "تعذر التحقق" }); }
    }, 400);
    return () => clearTimeout(t);
  }, [formula]);

  const slug = (s: string) => (s.replace(/[^a-zA-Z0-9]+/g, "_").replace(/^_+|_+$/g, "") || "calc") + "_" + Math.abs(Array.from(formula).reduce((h, c) => (h * 31 + c.charCodeAt(0)) | 0, 7)).toString(36).slice(0, 4);

  const add = async () => {
    setBusy(true);
    try {
      await addReportField(reportId, {
        fieldType: "CalculatedField", fieldCode: slug(nameEn || nameAr),
        displayNameEn: nameEn || nameAr, displayNameAr: nameAr || nameEn,
        calculationText: formula, formatPattern: format || null, width: 120, sortOrder,
      });
      setNameAr(""); setNameEn(""); setFormula(""); setFormat(""); setValid(null); setOpen(false);
      await onAdded(); toast.success("تمت إضافة الحقل المحسوب");
    } catch { /* surfaced by apiFetch */ }
    finally { setBusy(false); }
  };

  if (!open) return <button onClick={() => setOpen(true)} className="text-sm text-primary">＋ حقل محسوب</button>;
  return (
    <div className="border border-border bg-card p-4 space-y-3 max-w-2xl">
      <h3 className="font-semibold">حقل محسوب</h3>
      <div className="grid gap-2 md:grid-cols-2">
        <input value={nameAr} onChange={(e) => setNameAr(e.target.value)} placeholder="الاسم (عربي)" className={CLS_INPUT} />
        <input value={nameEn} onChange={(e) => setNameEn(e.target.value)} placeholder="الاسم (إنجليزي)" className={CLS_INPUT} />
      </div>
      <textarea value={formula} onChange={(e) => setFormula(e.target.value)} placeholder="مثال: ROUND(basicSalary * 0.09, 2)" rows={2}
        className="w-full border border-border bg-background px-3 py-2 text-sm font-mono" dir="ltr" />
      {valid && (valid.isValid
        ? <p className="text-xs text-green-600">صيغة صحيحة ✓</p>
        : <p className="text-xs text-destructive">{valid.error || "صيغة غير صحيحة"}</p>)}
      <input value={format} onChange={(e) => setFormat(e.target.value)} placeholder="نمط التنسيق (اختياري) مثل 0.00" className={CLS_INPUT} />
      <div className="flex items-center gap-2">
        <button onClick={add} disabled={busy || !nameAr && !nameEn || !valid?.isValid} className={CLS_BTN_PRIMARY}>{busy ? <Loader2 className="h-4 w-4 animate-spin" /> : null} أضف</button>
        <button onClick={() => setOpen(false)} className="border border-border bg-secondary px-4 py-2 text-sm">إلغاء</button>
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

function CodePicker({ fields, label, onPick }: { fields: ReportDefinition["fields"]; label: string; onPick: (code: string) => Promise<void> }) {
  const [code, setCode] = useState("");
  return (
    <div className="flex items-center gap-2">
      <select value={code} onChange={(e) => setCode(e.target.value)} className={`${CLS_INPUT} flex-1`}>
        <option value="">— حقل —</option>
        {fields.map((f) => <option key={f.id} value={f.fieldCode}>{f.displayNameAr || f.displayNameEn}</option>)}
      </select>
      <button disabled={!code} onClick={() => code && onPick(code)} className={CLS_BTN_PRIMARY}>{label}</button>
    </div>
  );
}

function FilterAdder({ fields, onAdd }: { fields: ReportDefinition["fields"]; onAdd: (b: { fieldCode: string; operator: ReportFilterOperator; value: string; valueTo?: string; isParameter: boolean }) => Promise<void> }) {
  const [code, setCode] = useState(""); const [op, setOp] = useState<ReportFilterOperator>("Equals");
  const [value, setValue] = useState(""); const [valueTo, setValueTo] = useState(""); const [isParameter, setIsParameter] = useState(false);
  return (
    <div className="flex flex-wrap items-center gap-2">
      <select value={code} onChange={(e) => setCode(e.target.value)} className={CLS_INPUT}>
        <option value="">— حقل —</option>
        {fields.map((f) => <option key={f.id} value={f.fieldCode}>{f.displayNameAr || f.displayNameEn}</option>)}
      </select>
      <select value={op} onChange={(e) => setOp(e.target.value as ReportFilterOperator)} className={CLS_INPUT}>
        {OPERATORS.map((o) => <option key={o.v} value={o.v}>{o.l}</option>)}
      </select>
      <input value={value} onChange={(e) => setValue(e.target.value)} placeholder="القيمة" className={CLS_INPUT} />
      {op === "Between" && <input value={valueTo} onChange={(e) => setValueTo(e.target.value)} placeholder="إلى" className={CLS_INPUT} />}
      <label className="flex items-center gap-1 text-xs"><input type="checkbox" checked={isParameter} onChange={(e) => setIsParameter(e.target.checked)} /> معامل وقت التشغيل</label>
      <button disabled={!code} onClick={() => code && onAdd({ fieldCode: code, operator: op, value, valueTo: op === "Between" ? valueTo : undefined, isParameter })} className={CLS_BTN_PRIMARY}>أضف</button>
    </div>
  );
}
```

- [ ] **Step 2: Build.** Run: `npx next build`
Expected: compiles with 0 TypeScript errors; route `ƒ /reports/builder/[[...id]]` registered.

> If `JoinType` is not exported from `@/lib/api/reports`, confirm its name by opening the client (it is declared as `export type JoinType = "Inner" | "Left" | "Right";`). If `addReportRelationship`/`deleteReportRelationship`/`getReportRelationships`/`validateFormula` are not exported, STOP and report — the plan assumes the merged canonical client (they are present at lines ~368-389 and ~484-488).

- [ ] **Step 3: Commit**

```bash
git add "src/app/(dashboard)/reports/builder"
git commit -m "feat(reports): builder joins step + joined-object/computed fields + runtime-parameter toggle"
```

---

## Task 3: Viewer — runtime parameters prompt

**Files:**
- Create: `src/components/reports/report-parameters.tsx`
- Modify: `src/app/(dashboard)/reports/[id]/page.tsx`

**Interfaces:**
- Consumes: `ReportDefinition` (`filters` with `isParameter`, `operator`, `fieldCode`, `value`, `valueTo`), `runReport(id, {page,pageSize,parameters})`, `exportReport(id, format, name, parameters)`.
- Produces: a parameters panel; when a report has `isParameter` filters, the viewer prompts and passes the values to run + export.

- [ ] **Step 1: Create `src/components/reports/report-parameters.tsx`:**

```tsx
"use client";

import { ReportFilter } from "@/lib/api/reports";

/**
 * Renders one input per runtime-parameter filter. Values are keyed by the parameter key the
 * backend expects: `fieldCode`, plus `fieldCode:to` for a Between filter's upper bound.
 */
export function ReportParameters({
  filters, values, onChange, onRun,
}: {
  filters: ReportFilter[];
  values: Record<string, string>;
  onChange: (key: string, value: string) => void;
  onRun: () => void;
}) {
  if (filters.length === 0) return null;
  return (
    <div className="border border-border bg-card p-4 space-y-3">
      <h3 className="font-semibold text-sm">معاملات التشغيل</h3>
      <div className="flex flex-wrap items-end gap-3">
        {filters.map((f) => (
          <div key={f.id} className="space-y-1">
            <label className="block text-xs text-muted-foreground">{f.fieldCode}</label>
            <div className="flex items-center gap-1">
              <input
                value={values[f.fieldCode] ?? ""}
                onChange={(e) => onChange(f.fieldCode, e.target.value)}
                placeholder={f.operator === "Between" ? "من" : "القيمة"}
                className="h-9 w-40 border border-border bg-background px-3 text-sm"
              />
              {f.operator === "Between" && (
                <input
                  value={values[`${f.fieldCode}:to`] ?? ""}
                  onChange={(e) => onChange(`${f.fieldCode}:to`, e.target.value)}
                  placeholder="إلى"
                  className="h-9 w-40 border border-border bg-background px-3 text-sm"
                />
              )}
            </div>
          </div>
        ))}
        <button onClick={onRun} className="inline-flex h-9 items-center gap-2 bg-primary px-4 text-sm text-primary-foreground">تشغيل</button>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Replace the viewer** `src/app/(dashboard)/reports/[id]/page.tsx` with the parameter-aware version:

```tsx
"use client";

import { use, useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { AlertTriangle, Loader2, Pencil, RefreshCw } from "lucide-react";
import { toast } from "sonner";
import { usePermission } from "@/lib/permissions";
import { getReport, runReport, exportReport, ReportDefinition, ReportResult, ExportFormat } from "@/lib/api/reports";
import { ReportTable } from "@/components/reports/report-table";
import { SchedulePanel } from "@/components/reports/schedule-panel";
import { ReportParameters } from "@/components/reports/report-parameters";

const FORMATS: { key: ExportFormat; label: string }[] = [
  { key: "excel", label: "Excel" }, { key: "csv", label: "CSV" }, { key: "pdf", label: "PDF" },
  { key: "sif", label: "WPS/SIF" },
];

export default function ReportViewerPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { allowed: canEdit } = usePermission("Platform.Reports.Edit");
  const { allowed: canExport } = usePermission("Platform.Reports.Export");
  const [report, setReport] = useState<ReportDefinition | null>(null);
  const [result, setResult] = useState<ReportResult | null>(null);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<string | null>(null);
  // Draft inputs (edited in the panel) vs applied (what the current run used).
  const [draft, setDraft] = useState<Record<string, string>>({});
  const [applied, setApplied] = useState<Record<string, string>>({});

  const paramFilters = useMemo(() => (report?.filters ?? []).filter((f) => f.isParameter), [report?.filters]);

  const nonBlank = (m: Record<string, string>) => Object.fromEntries(Object.entries(m).filter(([, v]) => v !== "" && v != null));

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const parameters = nonBlank(applied);
      const [r, res] = await Promise.all([getReport(id), runReport(id, { page, pageSize: 50, parameters })]);
      setReport(r); setResult(res);
    } catch { toast.error("تعذر تحميل التقرير"); }
    finally { setLoading(false); }
  }, [id, page, applied]);

  useEffect(() => { queueMicrotask(() => { load(); }); }, [load]);

  // Seed the draft inputs from the parameterized filters' stored defaults once, for UX.
  useEffect(() => {
    if (paramFilters.length === 0) return;
    setDraft((prev) => {
      if (Object.keys(prev).length > 0) return prev;
      const seed: Record<string, string> = {};
      for (const f of paramFilters) {
        if (f.value != null) seed[f.fieldCode] = f.value;
        if (f.operator === "Between" && f.valueTo != null) seed[`${f.fieldCode}:to`] = f.valueTo;
      }
      return seed;
    });
  }, [paramFilters]);

  const doExport = async (f: ExportFormat) => {
    setBusy(f);
    try { await exportReport(id, f, report?.code || "report", nonBlank(applied)); toast.success("تم بدء التنزيل"); }
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

      <ReportParameters
        filters={paramFilters}
        values={draft}
        onChange={(key, value) => setDraft((d) => ({ ...d, [key]: value }))}
        onRun={() => { setPage(1); setApplied(draft); }}
      />

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

      {canEdit && <SchedulePanel reportId={id} />}
    </div>
  );
}
```

- [ ] **Step 3: Build.** Run: `npx next build`
Expected: compiles with 0 TypeScript errors.

> Note `ReportFilter` must be exported from `@/lib/api/reports` (it is — the canonical client declares `export interface ReportFilter { id; fieldCode; operator; value?; valueTo?; logicalOperator?; isParameter; }`). If the viewer page receives `params` synchronously rather than as a Promise, match the sibling convention (the current file uses `use(params)`).

- [ ] **Step 4: Commit**

```bash
git add src/components/reports/report-parameters.tsx "src/app/(dashboard)/reports/[id]/page.tsx"
git commit -m "feat(reports): viewer runtime-parameter prompt feeding run + export"
```

---

## Final verification & deploy
- [ ] `dotnet build backend/src/HR.Api/HR.Api.csproj` → 0 errors.
- [ ] `npx next build` → 0 errors.
- [ ] Deploy backend once (perm change): `dotnet publish backend/src/HR.Api -c Release -o ./publish`, zip forward-slash entries (Python `zipfile`), `az webapp deploy --resource-group HR --name hrcloud-api-v4xd --src-path <zip> --type zip`. Push → Vercel auto-deploys FE. No migration.
- [ ] Live-verify: a report author lacking `Platform.Objects.View` can `GET /api/platform/objects` (200) but not POST (403); build a 2-object joined report with a computed field and a parameterized filter → preview; open the viewer, change a parameter, Run → results reflect it; export respects the parameter.

## Self-review notes (author)
- Spec Component 1 (perm) → Task 1. Components 2 (joins), 3 (joined + computed fields), 4 (param toggle) → Task 2. Component 5 (viewer prompt) → Task 3. All covered.
- No migration; canonical client unmodified (only consumed).
- Type consistency: `JoinType`/`ReportFilterOperator`/`ReportType`/`ReportScope` string unions used consistently; `objectDefinitionId` null for primary, registry Guid for joined; computed via `fieldType:"CalculatedField"` + `calculationText`; parameters keyed `fieldCode` / `fieldCode:to`.
- Known limits (carried): two fields sharing a code across joins still throws at run; parameter inputs are plain text.
