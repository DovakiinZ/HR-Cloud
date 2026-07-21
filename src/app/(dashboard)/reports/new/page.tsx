"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft, ArrowRight, Check, Loader2, Plus, Settings2, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { usePermission } from "@/lib/permissions";
import {
  getReportSubjects, getReportSubjectFields, createQuickReport,
  ReportSubject, ReportFieldDescriptor, ReportFilterOperator, ReportScope,
  QuickFilterInput,
} from "@/lib/api/reports";

// Friendly, business-facing operator labels.
const OPERATOR_LABELS: Partial<Record<ReportFilterOperator, string>> = {
  Equals: "يساوي",
  NotEquals: "لا يساوي",
  Contains: "يحتوي على",
  StartsWith: "يبدأ بـ",
  EndsWith: "ينتهي بـ",
  GreaterThan: "أكبر من",
  LessThan: "أصغر من",
  GreaterThanOrEqual: "أكبر أو يساوي",
  LessThanOrEqual: "أصغر أو يساوي",
  Between: "بين",
  In: "ضمن (افصل بفواصل)",
  NotIn: "ليس ضمن",
  IsNull: "فارغ",
  IsNotNull: "غير فارغ",
};

// Operators that take no value input.
const VALUELESS_OPERATORS: ReportFilterOperator[] = ["IsNull", "IsNotNull"];

// Field data types that should use a date picker.
const DATE_TYPES = new Set(["Date", "DateTime"]);

const SCOPES: { value: ReportScope; label: string }[] = [
  { value: "Company", label: "الشركة (يراه الجميع)" },
  { value: "Department", label: "الإدارة" },
  { value: "Personal", label: "خاص بي" },
];

type FilterRow = { fieldKey: string; operator: ReportFilterOperator; value: string; valueTo: string; isParameter: boolean };

const STEPS = ["الموضوع", "الحقول", "التصفية", "الحفظ"];

export default function NewSimpleReportPage() {
  const router = useRouter();
  const { allowed: canCreate, ready } = usePermission("Platform.Reports.Create");

  const [step, setStep] = useState(0);

  // Step 1 — subject
  const [subjects, setSubjects] = useState<ReportSubject[]>([]);
  const [loadingSubjects, setLoadingSubjects] = useState(true);
  const [subject, setSubject] = useState<ReportSubject | null>(null);

  // Step 2 — fields
  const [fields, setFields] = useState<ReportFieldDescriptor[]>([]);
  const [loadingFields, setLoadingFields] = useState(false);
  const [selected, setSelected] = useState<string[]>([]); // ordered field keys

  // Step 3 — filters
  const [filters, setFilters] = useState<FilterRow[]>([]);

  // Step 4 — save
  const [nameAr, setNameAr] = useState("");
  const [nameEn, setNameEn] = useState("");
  const [scope, setScope] = useState<ReportScope>("Company");
  const [groupByKey, setGroupByKey] = useState("");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    queueMicrotask(async () => {
      try { setSubjects(await getReportSubjects()); }
      catch { toast.error("تعذر تحميل مواضيع التقارير"); }
      finally { setLoadingSubjects(false); }
    });
  }, []);

  const pickSubject = useCallback(async (s: ReportSubject) => {
    setSubject(s);
    setSelected([]); setFilters([]); setGroupByKey("");
    setLoadingFields(true);
    try {
      const fs = await getReportSubjectFields(s.key);
      setFields(fs);
      // Pre-select the default columns (collision-free), like a sensible starter report.
      const used = new Set<string>();
      const defaults: string[] = [];
      for (const f of fs) {
        if (!f.isDefault) continue;
        if (used.has(f.propertyPath)) continue;
        used.add(f.propertyPath); defaults.push(f.key);
      }
      setSelected(defaults);
      setNameAr(`${s.labelAr} — تقرير`);
      setNameEn(`${s.labelEn} — Report`);
      setStep(1);
    } catch { toast.error("تعذر تحميل الحقول"); }
    finally { setLoadingFields(false); }
  }, []);

  const byKey = useMemo(() => new Map(fields.map((f) => [f.key, f])), [fields]);

  // Columns already occupying a physical column (R1 can't select two fields that share one).
  const usedColumns = useMemo(() => {
    const m = new Map<string, string>(); // propertyPath → owning key
    for (const k of selected) { const f = byKey.get(k); if (f) m.set(f.propertyPath, k); }
    return m;
  }, [selected, byKey]);

  const toggleField = (f: ReportFieldDescriptor) => {
    setSelected((prev) => {
      if (prev.includes(f.key)) return prev.filter((k) => k !== f.key);
      const owner = usedColumns.get(f.propertyPath);
      if (owner && owner !== f.key) {
        toast.error("لا يمكن الجمع بين حقلين يشيران إلى نفس العمود في هذا الإصدار");
        return prev;
      }
      return [...prev, f.key];
    });
  };

  const groups = useMemo(() => {
    const g = new Map<string, ReportFieldDescriptor[]>();
    for (const f of fields) { const arr = g.get(f.group) ?? []; arr.push(f); g.set(f.group, arr); }
    return [...g.entries()];
  }, [fields]);

  // Fields usable as filters/group-by in the simple builder = own fields (no join hop).
  const ownSelected = useMemo(
    () => selected.map((k) => byKey.get(k)).filter((f): f is ReportFieldDescriptor => !!f && f.joinPath.length === 0),
    [selected, byKey],
  );
  const filterableOwn = useMemo(() => fields.filter((f) => f.filterable && f.joinPath.length === 0), [fields]);
  const groupableSelected = useMemo(() => ownSelected.filter((f) => f.groupable), [ownSelected]);

  const addFilter = () => {
    const first = filterableOwn[0];
    if (!first) { toast.error("لا توجد حقول قابلة للتصفية"); return; }
    setFilters((f) => [...f, { fieldKey: first.key, operator: (first.allowedOperators[0] ?? "Equals"), value: "", valueTo: "", isParameter: false }]);
  };
  const updateFilter = (i: number, patch: Partial<FilterRow>) =>
    setFilters((f) => f.map((row, idx) => (idx === i ? { ...row, ...patch } : row)));
  const removeFilter = (i: number) => setFilters((f) => f.filter((_, idx) => idx !== i));

  const save = async () => {
    if (selected.length === 0) { toast.error("اختر حقلاً واحداً على الأقل"); return; }
    if (!nameAr.trim()) { toast.error("أدخل اسم التقرير"); return; }
    setSaving(true);
    const payloadFilters: QuickFilterInput[] = filters
      // Keep a filter if: it's value-less (IsNull/IsNotNull), a runtime parameter, or has a value.
      .filter((f) => VALUELESS_OPERATORS.includes(f.operator) || f.isParameter || f.value.trim() !== "")
      .map((f) => ({
        fieldKey: f.fieldKey,
        operator: f.operator,
        value: f.value.trim() === "" ? null : f.value.trim(),
        valueTo: f.operator === "Between" && f.valueTo.trim() !== "" ? f.valueTo.trim() : null,
        isParameter: f.isParameter,
      }));
    try {
      const res = await createQuickReport({
        nameAr: nameAr.trim(),
        nameEn: (nameEn.trim() || nameAr.trim()),
        scope,
        fieldKeys: selected,
        filters: payloadFilters,
        groupByKeys: groupByKey ? [groupByKey] : [],
      });
      const droppedKeys = [...res.skippedFieldKeys, ...res.skippedFilterKeys];
      if (droppedKeys.length > 0) {
        const labels = droppedKeys.map((k) => byKey.get(k)?.labelAr ?? k).join("، ");
        toast.warning(`تم إنشاء التقرير مع تجاهل: ${labels}`, { duration: 6000 });
      } else {
        toast.success("تم إنشاء التقرير");
      }
      router.push(`/reports/${res.report.id}`);
    } catch {
      toast.error("تعذر إنشاء التقرير");
      setSaving(false);
    }
  };

  if (ready && !canCreate) {
    return (
      <div dir="rtl" className="border border-border bg-card p-12 text-center">
        <p className="text-sm text-muted-foreground">ليس لديك صلاحية إنشاء التقارير.</p>
      </div>
    );
  }

  return (
    <div className="space-y-6" dir="rtl">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold">تقرير جديد</h1>
          <p className="text-sm text-muted-foreground mt-1">أنشئ تقريراً في خطوات بسيطة — اختر الموضوع والحقول فقط.</p>
        </div>
        <Link href="/reports/builder" className="inline-flex h-9 items-center gap-2 border border-border bg-secondary px-3 text-sm hover:bg-secondary/70">
          <Settings2 className="h-4 w-4" /> المنشئ المتقدم
        </Link>
      </div>

      {/* Stepper */}
      <div className="flex items-center gap-2 text-sm">
        {STEPS.map((label, i) => (
          <div key={label} className="flex items-center gap-2">
            <div className={`flex h-7 w-7 items-center justify-center rounded-full text-xs ${i < step ? "bg-primary text-primary-foreground" : i === step ? "border-2 border-primary text-primary" : "border border-border text-muted-foreground"}`}>
              {i < step ? <Check className="h-3.5 w-3.5" /> : i + 1}
            </div>
            <span className={i === step ? "font-medium" : "text-muted-foreground"}>{label}</span>
            {i < STEPS.length - 1 && <div className="h-px w-6 bg-border" />}
          </div>
        ))}
      </div>

      {/* Step 1 — Subject */}
      {step === 0 && (
        <div className="space-y-4">
          <h2 className="text-lg font-semibold">ما هو موضوع التقرير؟</h2>
          {loadingSubjects ? (
            <div className="border border-border bg-card p-12 flex items-center justify-center"><Loader2 className="h-8 w-8 animate-spin text-muted-foreground" /></div>
          ) : subjects.length === 0 ? (
            <div className="border border-border bg-card p-12 text-center text-sm text-muted-foreground">
              لا توجد مواضيع متاحة. تأكد من تسجيل الكيانات القابلة للتقارير أولاً.
            </div>
          ) : (
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
              {subjects.map((s) => (
                <button key={s.key} onClick={() => pickSubject(s)}
                  className="group flex flex-col items-start gap-1 border border-border bg-card p-4 text-right hover:border-primary hover:bg-secondary/40">
                  <span className="text-base font-semibold">{s.labelAr}</span>
                  <span className="text-xs text-muted-foreground">{s.labelEn}</span>
                </button>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Step 2 — Fields */}
      {step === 1 && subject && (
        <div className="space-y-4">
          <div className="flex items-center justify-between">
            <h2 className="text-lg font-semibold">اختر الأعمدة — {subject.labelAr}</h2>
            <span className="text-xs text-muted-foreground">تم اختيار {selected.length} حقلاً</span>
          </div>
          {loadingFields ? (
            <div className="border border-border bg-card p-12 flex items-center justify-center"><Loader2 className="h-8 w-8 animate-spin text-muted-foreground" /></div>
          ) : (
            <div className="space-y-5">
              {groups.map(([group, fs]) => (
                <div key={group} className="space-y-2">
                  <div className="text-xs font-medium uppercase tracking-wider text-muted-foreground">{group}</div>
                  <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
                    {fs.map((f) => {
                      const isSel = selected.includes(f.key);
                      const owner = usedColumns.get(f.propertyPath);
                      const blocked = !isSel && !!owner && owner !== f.key;
                      return (
                        <label key={f.key}
                          className={`flex items-start gap-2 border p-2.5 text-sm ${blocked ? "border-border/50 opacity-50 cursor-not-allowed" : "border-border cursor-pointer hover:bg-secondary/40"} ${isSel ? "border-primary bg-secondary/40" : ""}`}>
                          <input type="checkbox" checked={isSel} disabled={blocked} onChange={() => toggleField(f)} className="mt-0.5" />
                          <span className="flex flex-col">
                            <span>{f.labelAr}</span>
                            <span className="text-[11px] text-muted-foreground">
                              {f.joinPath.length > 0 ? "من جدول مرتبط" : f.aggregatable ? "قيمة رقمية" : "‎"}
                            </span>
                          </span>
                        </label>
                      );
                    })}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Step 3 — Filters */}
      {step === 2 && (
        <div className="space-y-4">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-lg font-semibold">التصفية (اختياري)</h2>
              <p className="text-xs text-muted-foreground mt-1">حدّد الصفوف التي تريد ظهورها. اترك القيمة فارغة وفعّل «اسأل عند التشغيل» ليدخلها المستخدم كل مرة.</p>
            </div>
            <button onClick={addFilter} className="inline-flex h-8 items-center gap-1.5 border border-border bg-secondary px-2.5 text-xs hover:bg-secondary/70"><Plus className="h-3.5 w-3.5" /> إضافة تصفية</button>
          </div>
          {filters.length === 0 ? (
            <div className="border border-dashed border-border bg-card p-8 text-center text-sm text-muted-foreground">لا توجد تصفية — سيعرض التقرير كل الصفوف.</div>
          ) : (
            <div className="space-y-2">
              {filters.map((row, i) => {
                const f = byKey.get(row.fieldKey);
                const ops = f?.allowedOperators ?? ["Equals"];
                const valueless = VALUELESS_OPERATORS.includes(row.operator);
                const inputType = f && DATE_TYPES.has(f.dataType) ? "date" : "text";
                return (
                  <div key={i} className="flex flex-wrap items-center gap-2 border border-border bg-card p-2.5">
                    <select value={row.fieldKey} onChange={(e) => updateFilter(i, { fieldKey: e.target.value, operator: (byKey.get(e.target.value)?.allowedOperators[0] ?? "Equals") })}
                      className="h-8 border border-border bg-background px-2 text-sm">
                      {filterableOwn.map((of) => <option key={of.key} value={of.key}>{of.labelAr}</option>)}
                    </select>
                    <select value={row.operator} onChange={(e) => updateFilter(i, { operator: e.target.value as ReportFilterOperator })}
                      className="h-8 border border-border bg-background px-2 text-sm">
                      {ops.filter((o) => OPERATOR_LABELS[o]).map((o) => <option key={o} value={o}>{OPERATOR_LABELS[o]}</option>)}
                    </select>
                    {!valueless && (
                      <input type={inputType} value={row.value} onChange={(e) => updateFilter(i, { value: e.target.value })} placeholder="القيمة" disabled={row.isParameter}
                        className="h-8 flex-1 min-w-24 border border-border bg-background px-2 text-sm disabled:opacity-50" />
                    )}
                    {!valueless && row.operator === "Between" && (
                      <input type={inputType} value={row.valueTo} onChange={(e) => updateFilter(i, { valueTo: e.target.value })} placeholder="إلى" disabled={row.isParameter}
                        className="h-8 w-32 border border-border bg-background px-2 text-sm disabled:opacity-50" />
                    )}
                    {!valueless && (
                      <label className="flex items-center gap-1.5 text-xs text-muted-foreground">
                        <input type="checkbox" checked={row.isParameter} onChange={(e) => updateFilter(i, { isParameter: e.target.checked })} /> اسأل عند التشغيل
                      </label>
                    )}
                    <button onClick={() => removeFilter(i)} className="text-muted-foreground hover:text-destructive"><Trash2 className="h-4 w-4" /></button>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      )}

      {/* Step 4 — Save */}
      {step === 3 && (
        <div className="space-y-4 max-w-xl">
          <h2 className="text-lg font-semibold">تفاصيل التقرير</h2>
          <div className="space-y-1">
            <label className="text-sm font-medium">اسم التقرير (عربي)</label>
            <input value={nameAr} onChange={(e) => setNameAr(e.target.value)} className="h-9 w-full border border-border bg-background px-3 text-sm" />
          </div>
          <div className="space-y-1">
            <label className="text-sm font-medium">Report name (English)</label>
            <input value={nameEn} onChange={(e) => setNameEn(e.target.value)} dir="ltr" className="h-9 w-full border border-border bg-background px-3 text-sm" />
          </div>
          <div className="space-y-1">
            <label className="text-sm font-medium">من يمكنه رؤيته</label>
            <select value={scope} onChange={(e) => setScope(e.target.value as ReportScope)} className="h-9 w-full border border-border bg-background px-3 text-sm">
              {SCOPES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
            </select>
          </div>
          {groupableSelected.length > 0 && (
            <div className="space-y-1">
              <label className="text-sm font-medium">تجميع حسب (اختياري)</label>
              <select value={groupByKey} onChange={(e) => setGroupByKey(e.target.value)} className="h-9 w-full border border-border bg-background px-3 text-sm">
                <option value="">بدون تجميع</option>
                {groupableSelected.map((f) => <option key={f.key} value={f.key}>{f.labelAr}</option>)}
              </select>
              <p className="text-[11px] text-muted-foreground">عند التجميع، تُجمع القيم الرقمية تلقائياً لكل مجموعة.</p>
            </div>
          )}
          <div className="border border-border bg-secondary/30 p-3 text-xs text-muted-foreground">
            {selected.length} عمود · {filters.length} تصفية{groupByKey ? " · مُجمّع" : ""}
          </div>
        </div>
      )}

      {/* Nav */}
      <div className="flex items-center justify-between border-t border-border pt-4">
        <button onClick={() => setStep((s) => Math.max(0, s - 1))} disabled={step === 0}
          className="inline-flex h-9 items-center gap-2 border border-border bg-secondary px-3 text-sm hover:bg-secondary/70 disabled:opacity-40">
          <ArrowRight className="h-4 w-4" /> السابق
        </button>
        {step < STEPS.length - 1 ? (
          <button onClick={() => setStep((s) => s + 1)} disabled={step === 0 ? !subject : step === 1 ? selected.length === 0 : false}
            className="inline-flex h-9 items-center gap-2 bg-primary px-4 text-sm text-primary-foreground hover:bg-primary/90 disabled:opacity-40">
            التالي <ArrowLeft className="h-4 w-4" />
          </button>
        ) : (
          <button onClick={save} disabled={saving}
            className="inline-flex h-9 items-center gap-2 bg-primary px-4 text-sm text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
            {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />} إنشاء التقرير
          </button>
        )}
      </div>
    </div>
  );
}
