"use client";

import { use, useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { Loader2, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import {
  getReport, createReport, updateReport, publishReport, runReport,
  addReportField, deleteReportField, addReportFilter, deleteReportFilter,
  addReportGrouping, deleteReportGrouping, addReportSorting, deleteReportSorting,
  getSelectableObjects,
  ReportDefinition, ReportResult, SelectableObject, CatalogField,
  ReportType, ReportScope, ReportFilterOperator, SortDirection,
} from "@/lib/api/reports";
import { ReportTable } from "@/components/reports/report-table";

// Shorthand inline class strings replacing the plan's `input` / `btn-primary` shorthand
// (those utilities are NOT defined in this project's globals.css).
// Derived from the real classes used in settings/company-organization/page.tsx
// and the pattern used in reports/page.tsx + reports/[id]/page.tsx.
const CLS_INPUT = "h-9 w-full border border-border bg-background px-3 text-sm";
const CLS_BTN_PRIMARY = "inline-flex h-9 items-center gap-2 bg-primary px-4 text-sm text-primary-foreground disabled:opacity-50";

const REPORT_TYPES: { v: ReportType; l: string }[] = [
  { v: "Tabular", l: "جدولي" },
  { v: "Summary", l: "ملخّص" },
  { v: "Matrix", l: "مصفوفة" },
  { v: "Chart", l: "مخطط" },
];
const SCOPES: { v: ReportScope; l: string }[] = [
  { v: "Personal", l: "شخصي" },
  { v: "Company", l: "الشركة" },
  { v: "Department", l: "قسم" },
  { v: "Shared", l: "مشترك" },
];
const OPERATORS: { v: ReportFilterOperator; l: string }[] = [
  { v: "Equals", l: "يساوي" },
  { v: "NotEquals", l: "لا يساوي" },
  { v: "Contains", l: "يحتوي" },
  { v: "StartsWith", l: "يبدأ بـ" },
  { v: "EndsWith", l: "ينتهي بـ" },
];

export default function ReportBuilderPage({ params }: { params: Promise<{ id?: string[] }> }) {
  const { id: idParam } = use(params);
  const router = useRouter();
  const existingId = idParam?.[0];

  const [step, setStep] = useState(0);
  const [reportId, setReportId] = useState<string | undefined>(existingId);
  const [report, setReport] = useState<ReportDefinition | null>(null);
  const [selectableObjects, setSelectableObjects] = useState<SelectableObject[]>([]);
  const [selectedObject, setSelectedObject] = useState<SelectableObject | null>(null);
  const [saving, setSaving] = useState(false);
  const [preview, setPreview] = useState<ReportResult | null>(null);

  // basics form
  const [form, setForm] = useState<{
    code: string; nameEn: string; nameAr: string; description: string;
    reportType: ReportType; scope: ReportScope; primaryObjectId: string;
  }>({ code: "", nameEn: "", nameAr: "", description: "", reportType: "Tabular", scope: "Company", primaryObjectId: "" });

  useEffect(() => { queueMicrotask(async () => {
    let activeObjects: SelectableObject[] = [];
    try {
      activeObjects = await getSelectableObjects();
      setSelectableObjects(activeObjects);
    } catch { toast.error("تعذر تحميل الكائنات"); }
    if (existingId) {
      try {
        const r = await getReport(existingId);
        setReport(r);
        setForm({ code: r.code, nameEn: r.nameEn, nameAr: r.nameAr, description: r.description ?? "", reportType: r.reportType, scope: r.scope, primaryObjectId: r.primaryObjectId });
        // In edit mode, find the matching SelectableObject by id to drive the fields panel.
        const found = activeObjects.find((sel) => sel.id === r.primaryObjectId) ?? null;
        setSelectedObject(found);
      } catch { toast.error("تعذر تحميل التقرير"); }
    }
  }); }, [existingId]);

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
              <select value={form.primaryObjectId} onChange={(e) => {
                const sel = selectableObjects.find((o) => o.id === e.target.value) ?? null;
                setForm({ ...form, primaryObjectId: sel ? sel.id : "" });
                setSelectedObject(sel);
              }} className={CLS_INPUT}>
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

      {/* Step 1 — Fields */}
      {step === 1 && reportId && (
        <FieldsStep report={report} selectedObject={selectedObject} reportId={reportId} onChange={refreshReport} />
      )}

      {/* Step 2 — Filters */}
      {step === 2 && reportId && (
        <ChildList
          title="عوامل التصفية"
          items={(report?.filters ?? []).map((f) => ({ id: f.id, label: `${f.fieldCode} ${f.operator} ${f.value ?? ""}` }))}
          onDelete={async (fid) => { await deleteReportFilter(fid); await refreshReport(); }}
          adder={<FilterAdder fields={report?.fields ?? []} onAdd={async (b) => { await addReportFilter(reportId, b); await refreshReport(); }} />}
        />
      )}

      {/* Step 3 — Grouping + Sorting */}
      {step === 3 && reportId && (
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

      {/* Step 4 — Preview */}
      {step === 4 && reportId && (
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

function FieldsStep({ report, selectedObject, reportId, onChange }: { report: ReportDefinition | null; selectedObject: SelectableObject | null; reportId: string; onChange: () => Promise<void> }) {
  const availableFields: CatalogField[] = selectedObject?.catalog.fields ?? [];
  return (
    <div className="grid gap-6 md:grid-cols-2">
      <div className="border border-border bg-card p-4">
        <h3 className="font-semibold mb-3">الحقول المتاحة</h3>
        {availableFields.length === 0 && <p className="text-sm text-muted-foreground">لا توجد حقول محمّلة. ارجع للأساسيات لاختيار الكائن.</p>}
        <ul className="space-y-1 max-h-96 overflow-auto">
          {availableFields.map((f) => {
            const isMeasure = f.isMeasure;
            return (
              <li key={f.code} className="flex items-center justify-between">
                <span className="text-sm">{f.nameAr || f.nameEn}</span>
                <button className="text-primary" title="أضف"
                  onClick={async () => { await addReportField(reportId, { fieldType: isMeasure ? "AggregateField" : "ObjectField", fieldCode: f.code, displayNameEn: f.nameEn, displayNameAr: f.nameAr, aggregation: isMeasure ? "Sum" : null, width: 120, sortOrder: (report?.fields.length ?? 0) }); await onChange(); }}>
                  <Plus className="h-4 w-4" />
                </button>
              </li>
            );
          })}
        </ul>
      </div>
      <div className="border border-border bg-card p-4">
        <h3 className="font-semibold mb-3">الحقول المختارة</h3>
        <ul className="space-y-1">
          {(report?.fields ?? []).map((f) => (
            <li key={f.id} className="flex items-center justify-between">
              <span className="text-sm">{f.displayNameAr || f.displayNameEn}{f.aggregation ? ` (${f.aggregation})` : ""}</span>
              <button className="text-destructive" onClick={async () => { await deleteReportField(f.id); await onChange(); }}><Trash2 className="h-4 w-4" /></button>
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

function FilterAdder({ fields, onAdd }: { fields: ReportDefinition["fields"]; onAdd: (b: { fieldCode: string; operator: ReportFilterOperator; value: string }) => Promise<void> }) {
  const [code, setCode] = useState(""); const [op, setOp] = useState<ReportFilterOperator>("Equals"); const [value, setValue] = useState("");
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
      <button disabled={!code} onClick={() => code && onAdd({ fieldCode: code, operator: op, value })} className={CLS_BTN_PRIMARY}>أضف</button>
    </div>
  );
}
