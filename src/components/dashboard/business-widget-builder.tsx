"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import React from "react";
import {
  BarChart2,
  Check,
  ChevronLeft,
  ChevronRight,
  Circle,
  Loader2,
  PieChart,
  Table2,
  TrendingUp,
  X,
  LayoutDashboard,
  Building2,
  Users,
  Briefcase,
  Calendar,
  DollarSign,
  ClipboardList,
  Star,
  Layers,
  Activity,
} from "lucide-react";
import { toast } from "sonner";
import {
  getDomains,
  getMetrics,
  getCatalogObjects,
  SemanticDomain,
  SemanticMetric,
  SemanticField,
} from "@/lib/api/catalog";
import { addWidgetFromMetric, previewMetric } from "@/lib/api/dashboards";
import { WidgetFilterSpec, WidgetDataResult } from "@/types/dashboard";
import { WidgetRenderer } from "./widget-renderer";

// ── Step labels ──────────────────────────────────────────────────────────────
const STEPS = ["نوع العنصر", "البيانات", "ما تريد عرضه", "التصفية", "الحفظ"];

// ── Visualization options ────────────────────────────────────────────────────
interface VizOption { viz: string; labelAr: string; icon: React.ReactNode }

const VIZ_OPTIONS: VizOption[] = [
  { viz: "KpiCard",   labelAr: "بطاقة مؤشر",  icon: <LayoutDashboard className="h-6 w-6" /> },
  { viz: "BarChart",  labelAr: "أعمدة",        icon: <BarChart2 className="h-6 w-6" /> },
  { viz: "LineChart", labelAr: "خط بياني",     icon: <TrendingUp className="h-6 w-6" /> },
  { viz: "PieChart",  labelAr: "دائري",        icon: <PieChart className="h-6 w-6" /> },
  { viz: "DonutChart",labelAr: "حلقي",         icon: <Circle className="h-6 w-6" /> },
  { viz: "Table",     labelAr: "جدول",         icon: <Table2 className="h-6 w-6" /> },
];

// ── Icon resolver for catalog icon strings ───────────────────────────────────
function CatalogIcon({ name, className }: { name?: string | null; className?: string }) {
  const cls = className ?? "h-5 w-5";
  const icons: Record<string, React.ReactNode> = {
    users:        <Users className={cls} />,
    user:         <Users className={cls} />,
    building:     <Building2 className={cls} />,
    briefcase:    <Briefcase className={cls} />,
    calendar:     <Calendar className={cls} />,
    dollar:       <DollarSign className={cls} />,
    clipboard:    <ClipboardList className={cls} />,
    star:         <Star className={cls} />,
    layers:       <Layers className={cls} />,
    activity:     <Activity className={cls} />,
    barchart:     <BarChart2 className={cls} />,
    piechart:     <PieChart className={cls} />,
    table:        <Table2 className={cls} />,
  };
  if (!name) return <Layers className={cls} />;
  const key = name.toLowerCase().replace(/[-_\s]/g, "");
  for (const k of Object.keys(icons)) {
    if (key.includes(k)) return <>{icons[k]}</>;
  }
  return <Layers className={cls} />;
}

// ── Props ────────────────────────────────────────────────────────────────────
export function BusinessWidgetBuilder({
  dashboardId,
  onSaved,
  onCancel,
  onAdvanced,
}: {
  dashboardId: string;
  onSaved: () => void;
  onCancel: () => void;
  onAdvanced: () => void;
}): React.JSX.Element {
  const [step, setStep] = useState(0);

  // Step 0
  const [visualization, setVisualization] = useState("");
  const [vizOverridden, setVizOverridden] = useState(false);

  // Step 1
  const [domains, setDomains] = useState<SemanticDomain[]>([]);
  const [loadingDomains, setLoadingDomains] = useState(false);
  const [domain, setDomain] = useState("");

  // Step 2
  const [metrics, setMetrics] = useState<SemanticMetric[]>([]);
  const [loadingMetrics, setLoadingMetrics] = useState(false);
  const [metricCode, setMetricCode] = useState("");
  const [chosenMetric, setChosenMetric] = useState<SemanticMetric | null>(null);

  // Step 3 — filters
  const [domainFields, setDomainFields] = useState<SemanticField[]>([]);
  const [filters, setFilters] = useState<WidgetFilterSpec[]>([]);

  // Step 4
  const [titleAr, setTitleAr] = useState("");
  const [saving, setSaving] = useState(false);

  // Preview
  const [previewResult, setPreviewResult] = useState<WidgetDataResult | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const previewTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // ── Load domains on mount ─────────────────────────────────────────────────
  useEffect(() => {
    setLoadingDomains(true);
    getDomains()
      .then(setDomains)
      .catch(() => setDomains([]))
      .finally(() => setLoadingDomains(false));
  }, []);

  // ── Load metrics when domain changes ──────────────────────────────────────
  useEffect(() => {
    if (!domain) return;
    setLoadingMetrics(true);
    setMetrics([]);
    setMetricCode("");
    setChosenMetric(null);
    getMetrics(domain)
      .then(setMetrics)
      .catch(() => setMetrics([]))
      .finally(() => setLoadingMetrics(false));
  }, [domain]);

  // ── Load domain fields when domain changes (for filter step) ─────────────
  useEffect(() => {
    if (!domain) return;
    getCatalogObjects(domain)
      .then((objs) => {
        // Collect all unique fields across domain objects
        const fieldMap = new Map<string, SemanticField>();
        for (const obj of objs) {
          for (const f of obj.fields) {
            if (!fieldMap.has(f.fieldCode)) fieldMap.set(f.fieldCode, f);
          }
        }
        setDomainFields([...fieldMap.values()]);
      })
      .catch(() => setDomainFields([]));
  }, [domain]);

  // ── Debounced preview ─────────────────────────────────────────────────────
  const triggerPreview = useCallback(
    (code: string, currentFilters: WidgetFilterSpec[], vis: string) => {
      if (previewTimerRef.current) clearTimeout(previewTimerRef.current);
      if (!code) {
        setPreviewResult(null);
        setPreviewError(null);
        return;
      }
      previewTimerRef.current = setTimeout(async () => {
        setPreviewLoading(true);
        setPreviewError(null);
        try {
          const result = await previewMetric(code, currentFilters, vis || undefined);
          setPreviewResult(result);
        } catch (e) {
          setPreviewError(e instanceof Error ? e.message : "تعذر جلب المعاينة");
          setPreviewResult(null);
        } finally {
          setPreviewLoading(false);
        }
      }, 400);
    },
    [],
  );

  // Trigger preview when metricCode / filters / visualization change
  useEffect(() => {
    if (metricCode) {
      triggerPreview(metricCode, filters, visualization);
    } else {
      setPreviewResult(null);
      setPreviewError(null);
    }
  }, [metricCode, filters, visualization, triggerPreview]);

  // Clear timer on unmount
  useEffect(() => () => { if (previewTimerRef.current) clearTimeout(previewTimerRef.current); }, []);

  // ── Metric selection — maybe pre-fill visualization ───────────────────────
  const selectMetric = (m: SemanticMetric) => {
    setMetricCode(m.code);
    setChosenMetric(m);
    if (!vizOverridden && m.defaultVisualization) {
      setVisualization(m.defaultVisualization);
    }
    if (!titleAr) setTitleAr(m.nameAr);
    // Reset filters when metric changes
    setFilters([]);
  };

  // ── Filter helpers ────────────────────────────────────────────────────────
  // Build candidate fields for the chosen metric
  const candidateFields: SemanticField[] = chosenMetric
    ? chosenMetric.suggestedFilterFields
        .map((fc) => domainFields.find((f) => f.fieldCode === fc))
        .filter((f): f is SemanticField => f !== undefined)
    : [];

  const isDateField = (f: SemanticField) =>
    f.role === "date" || f.fieldCode.toLowerCase().includes("date") || f.fieldCode.toLowerCase().includes("at");

  const setFilterValue = (fieldCode: string, operator: string, value: string) => {
    setFilters((prev) => {
      const without = prev.filter((f) => !(f.field === fieldCode && f.operator === operator));
      if (!value) return without;
      return [...without, { field: fieldCode, operator, value }];
    });
  };

  const getFilterValue = (fieldCode: string, operator: string): string => {
    return filters.find((f) => f.field === fieldCode && f.operator === operator)?.value ?? "";
  };

  const removeFilter = (fieldCode: string) => {
    setFilters((prev) => prev.filter((f) => f.field !== fieldCode));
  };

  // ── Step validity ─────────────────────────────────────────────────────────
  const stepValid = [
    !!visualization,          // Step 0
    !!domain,                 // Step 1
    !!metricCode,             // Step 2
    true,                     // Step 3 always valid
    titleAr.trim().length > 0,// Step 4
  ];

  const next = () => setStep((s) => Math.min(STEPS.length - 1, s + 1));
  const back = () => setStep((s) => Math.max(0, s - 1));

  // ── Save ─────────────────────────────────────────────────────────────────
  const handleSave = async () => {
    if (!metricCode || !visualization || !titleAr.trim()) return;
    setSaving(true);
    try {
      await addWidgetFromMetric(dashboardId, {
        metricCode,
        filters,
        visualization,
        dateGranularity: undefined,
        titleAr: titleAr.trim(),
        titleEn: titleAr.trim(),
      });
      toast.success("تم حفظ العنصر بنجاح");
      onSaved();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "تعذر حفظ العنصر");
    } finally {
      setSaving(false);
    }
  };

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <div className="flex h-full max-h-[85vh] flex-col" dir="rtl">
      {/* Stepper */}
      <div className="flex items-center gap-1 border-b border-border px-5 py-3 overflow-x-auto">
        {STEPS.map((label, i) => (
          <button
            key={label}
            onClick={() => i <= step && setStep(i)}
            className="flex items-center gap-1 shrink-0"
          >
            <span
              className={`flex h-6 w-6 items-center justify-center rounded-full text-xs font-bold ${
                i < step
                  ? "bg-primary text-primary-foreground"
                  : i === step
                  ? "border border-primary text-primary"
                  : "border border-border text-muted-foreground"
              }`}
            >
              {i < step ? <Check className="h-3 w-3" /> : i + 1}
            </span>
            <span
              className={`text-xs ${i === step ? "font-bold" : "text-muted-foreground"} ${
                i > step ? "" : "cursor-pointer"
              }`}
            >
              {label}
            </span>
            {i < STEPS.length - 1 && (
              <ChevronLeft className="mx-1 h-3 w-3 text-muted-foreground" />
            )}
          </button>
        ))}
      </div>

      {/* Body: controls + preview */}
      <div className="grid min-h-0 flex-1 grid-cols-1 lg:grid-cols-[1fr_360px]">
        {/* Controls */}
        <div className="min-h-0 overflow-auto p-5">
          {/* Step 0 — Widget Type */}
          {step === 0 && (
            <div className="space-y-4">
              <p className="text-sm text-muted-foreground">اختر نوع العنصر الذي تريد إضافته</p>
              <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
                {VIZ_OPTIONS.map(({ viz, labelAr, icon }) => (
                  <button
                    key={viz}
                    onClick={() => {
                      setVisualization(viz);
                      setVizOverridden(true);
                    }}
                    className={`flex flex-col items-center gap-2 border p-4 text-sm transition-colors ${
                      visualization === viz
                        ? "border-primary bg-primary/10 text-primary"
                        : "border-border hover:border-primary/50"
                    }`}
                  >
                    {icon}
                    <span className="font-medium">{labelAr}</span>
                  </button>
                ))}
              </div>
            </div>
          )}

          {/* Step 1 — Business Domain */}
          {step === 1 && (
            <div className="space-y-4">
              <p className="text-sm text-muted-foreground">اختر مجال البيانات الذي تريد عرضه</p>
              {loadingDomains ? (
                <div className="flex h-40 items-center justify-center text-muted-foreground">
                  <Loader2 className="h-5 w-5 animate-spin" />
                </div>
              ) : (
                <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
                  {domains.map((d) => (
                    <button
                      key={d.code}
                      onClick={() => setDomain(d.code)}
                      className={`flex flex-col items-start gap-2 border p-4 text-right text-sm transition-colors ${
                        domain === d.code
                          ? "border-primary bg-primary/10"
                          : "border-border hover:border-primary/50"
                      }`}
                    >
                      <CatalogIcon name={d.icon} />
                      <span className="font-medium">{d.nameAr}</span>
                      {d.descriptionAr && (
                        <span className="text-xs text-muted-foreground line-clamp-2">
                          {d.descriptionAr}
                        </span>
                      )}
                    </button>
                  ))}
                  {domains.length === 0 && (
                    <p className="col-span-3 text-sm text-muted-foreground">لا توجد مجالات متاحة</p>
                  )}
                </div>
              )}
            </div>
          )}

          {/* Step 2 — Metric */}
          {step === 2 && (
            <div className="space-y-4">
              <p className="text-sm text-muted-foreground">اختر المؤشر الذي تريد عرضه</p>
              {loadingMetrics ? (
                <div className="flex h-40 items-center justify-center text-muted-foreground">
                  <Loader2 className="h-5 w-5 animate-spin" />
                </div>
              ) : metrics.length === 0 ? (
                <p className="text-sm text-muted-foreground">لا توجد مؤشرات متاحة لهذا المجال</p>
              ) : (
                <div className="space-y-2">
                  {metrics.map((m) => (
                    <button
                      key={m.code}
                      onClick={() => selectMetric(m)}
                      className={`flex w-full items-start gap-3 border p-3 text-right text-sm transition-colors ${
                        metricCode === m.code
                          ? "border-primary bg-primary/10"
                          : "border-border hover:border-primary/50"
                      }`}
                    >
                      <span className="mt-0.5 shrink-0 text-muted-foreground">
                        <CatalogIcon name={m.icon} className="h-4 w-4" />
                      </span>
                      <span className="min-w-0 flex-1">
                        <span className="block font-medium">{m.nameAr}</span>
                        {m.descriptionAr && (
                          <span className="block text-xs text-muted-foreground">{m.descriptionAr}</span>
                        )}
                      </span>
                      {metricCode === m.code && (
                        <Check className="h-4 w-4 shrink-0 text-primary" />
                      )}
                    </button>
                  ))}
                </div>
              )}
            </div>
          )}

          {/* Step 3 — Filters (optional) */}
          {step === 3 && (
            <div className="space-y-4">
              <p className="text-sm text-muted-foreground">
                ضبط معايير التصفية (اختياري — يمكنك التخطي)
              </p>
              {candidateFields.length === 0 ? (
                <p className="text-sm text-muted-foreground">لا توجد حقول تصفية مقترحة لهذا المؤشر</p>
              ) : (
                <div className="space-y-4">
                  {candidateFields.map((f) => {
                    if (isDateField(f)) {
                      return (
                        <div key={f.fieldCode} className="space-y-1 border border-border p-3">
                          <div className="flex items-center justify-between">
                            <label className="text-xs font-bold uppercase tracking-wider text-muted-foreground">
                              {f.nameAr}
                            </label>
                            <button
                              onClick={() => removeFilter(f.fieldCode)}
                              className="text-muted-foreground hover:text-destructive"
                              title="إزالة"
                            >
                              <X className="h-3.5 w-3.5" />
                            </button>
                          </div>
                          <div className="flex gap-2">
                            <div className="flex-1 space-y-1">
                              <span className="text-xs text-muted-foreground">من</span>
                              <input
                                type="date"
                                value={getFilterValue(f.fieldCode, "gte")}
                                onChange={(e) =>
                                  setFilterValue(f.fieldCode, "gte", e.target.value)
                                }
                                className="h-9 w-full border border-border bg-background px-2 text-sm"
                                dir="ltr"
                              />
                            </div>
                            <div className="flex-1 space-y-1">
                              <span className="text-xs text-muted-foreground">إلى</span>
                              <input
                                type="date"
                                value={getFilterValue(f.fieldCode, "lte")}
                                onChange={(e) =>
                                  setFilterValue(f.fieldCode, "lte", e.target.value)
                                }
                                className="h-9 w-full border border-border bg-background px-2 text-sm"
                                dir="ltr"
                              />
                            </div>
                          </div>
                        </div>
                      );
                    }

                    // All other fields — text input
                    return (
                      <div key={f.fieldCode} className="space-y-1 border border-border p-3">
                        <div className="flex items-center justify-between">
                          <label className="text-xs font-bold uppercase tracking-wider text-muted-foreground">
                            {f.nameAr}
                          </label>
                          <button
                            onClick={() => removeFilter(f.fieldCode)}
                            className="text-muted-foreground hover:text-destructive"
                            title="إزالة"
                          >
                            <X className="h-3.5 w-3.5" />
                          </button>
                        </div>
                        <input
                          type="text"
                          value={getFilterValue(f.fieldCode, "eq")}
                          onChange={(e) =>
                            setFilterValue(f.fieldCode, "eq", e.target.value)
                          }
                          placeholder={`أدخل قيمة ${f.nameAr}…`}
                          className="h-9 w-full border border-border bg-background px-2 text-sm"
                        />
                        <p className="text-xs text-muted-foreground">
                          * اكتب القيمة مباشرة (اختيار المراجع متاح في الوضع المتقدم)
                        </p>
                      </div>
                    );
                  })}
                </div>
              )}

              {/* Active filter summary */}
              {filters.length > 0 && (
                <div className="border border-primary/30 bg-primary/5 p-3 text-xs">
                  <p className="mb-1 font-bold text-primary">فلاتر نشطة:</p>
                  <ul className="space-y-0.5">
                    {filters.map((fl, i) => (
                      <li key={i} className="text-muted-foreground" dir="ltr">
                        {fl.field} {fl.operator} &quot;{fl.value}&quot;
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          )}

          {/* Step 4 — Save */}
          {step === 4 && (
            <div className="space-y-4">
              <div className="space-y-1">
                <label className="text-xs font-bold uppercase tracking-wider text-muted-foreground">
                  عنوان العنصر <span className="text-destructive">*</span>
                </label>
                <input
                  value={titleAr}
                  onChange={(e) => setTitleAr(e.target.value)}
                  placeholder="مثال: مؤشر الغياب الشهري"
                  className="h-10 w-full border border-border bg-secondary px-3 text-sm"
                />
              </div>
              {/* Summary */}
              <div className="border border-border bg-secondary/40 p-3 text-xs text-muted-foreground space-y-1">
                <p>نوع العرض: <span className="text-foreground">{VIZ_OPTIONS.find((v) => v.viz === visualization)?.labelAr ?? visualization}</span></p>
                <p>المجال: <span className="text-foreground">{domains.find((d) => d.code === domain)?.nameAr ?? domain}</span></p>
                <p>المؤشر: <span className="text-foreground">{chosenMetric?.nameAr ?? metricCode}</span></p>
                <p>الفلاتر: <span className="text-foreground">{filters.length > 0 ? `${filters.length} فلتر نشط` : "بدون فلاتر"}</span></p>
              </div>
            </div>
          )}
        </div>

        {/* Live preview panel */}
        <div className="min-h-0 border-t border-border bg-background p-4 lg:border-r lg:border-t-0">
          <p className="mb-2 text-xs font-bold uppercase tracking-wider text-muted-foreground">
            معاينة مباشرة
          </p>
          <div className="h-64">
            {!metricCode ? (
              <div className="flex h-full items-center justify-center border border-dashed border-border text-sm text-muted-foreground text-center p-4">
                اختر مؤشراً لعرض المعاينة
              </div>
            ) : previewLoading ? (
              <div className="flex h-full items-center justify-center text-muted-foreground">
                <Loader2 className="h-5 w-5 animate-spin" />
              </div>
            ) : previewError ? (
              <div className="flex h-full flex-col items-center justify-center gap-2 border border-dashed border-destructive/50 text-center text-sm text-destructive p-4">
                <span>{previewError}</span>
              </div>
            ) : previewResult ? (
              <div className="h-full">
                <WidgetRenderer
                  type={visualization || "KpiCard"}
                  result={previewResult}
                />
              </div>
            ) : (
              <div className="flex h-full items-center justify-center border border-dashed border-border text-sm text-muted-foreground">
                لا توجد بيانات
              </div>
            )}
          </div>
          {/* Metric caption below preview */}
          {chosenMetric && (
            <div className="mt-3 text-xs text-muted-foreground">
              <p className="font-medium text-foreground">{chosenMetric.nameAr}</p>
              {chosenMetric.descriptionAr && <p className="mt-0.5">{chosenMetric.descriptionAr}</p>}
            </div>
          )}
        </div>
      </div>

      {/* Footer */}
      <div className="flex items-center justify-between border-t border-border px-5 py-3">
        <div className="flex items-center gap-2">
          <button
            onClick={onCancel}
            className="h-10 px-4 text-sm text-muted-foreground hover:text-foreground"
          >
            إلغاء
          </button>
          <button
            onClick={onAdvanced}
            className="h-10 border border-border px-4 text-sm text-muted-foreground hover:border-primary hover:text-primary"
          >
            متقدم
          </button>
        </div>
        <div className="flex items-center gap-2">
          {step > 0 && (
            <button
              onClick={back}
              className="flex h-10 items-center gap-1 border border-border px-4 text-sm hover:bg-muted"
            >
              <ChevronRight className="h-4 w-4" /> السابق
            </button>
          )}
          {step < STEPS.length - 1 ? (
            <button
              onClick={next}
              disabled={!stepValid[step]}
              className="flex h-10 items-center gap-1 bg-primary px-4 text-sm font-bold uppercase tracking-wider text-primary-foreground hover:bg-primary/80 disabled:opacity-40"
            >
              التالي <ChevronLeft className="h-4 w-4" />
            </button>
          ) : (
            <button
              onClick={handleSave}
              disabled={!stepValid[4] || saving}
              className="flex h-10 items-center gap-2 bg-primary px-5 text-sm font-bold uppercase tracking-wider text-primary-foreground hover:bg-primary/80 disabled:opacity-40"
            >
              {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
              حفظ العنصر
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
