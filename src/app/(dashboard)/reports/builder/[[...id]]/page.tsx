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
