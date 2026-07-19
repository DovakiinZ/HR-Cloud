"use client";

import { use, useCallback, useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import {
  ArrowRight,
  BarChart3,
  FileSpreadsheet,
  FileText,
  FileType,
  Loader2,
  Pencil,
} from "lucide-react";
import { toast } from "sonner";
import { AccessGuard } from "@/components/access/access-guard";
import { usePermissions } from "@/lib/permissions";
import {
  ReportParameterBar,
  ParameterDraft,
  draftToParameters,
  toKey,
  toUpperKey,
} from "@/components/reports/report-parameter-bar";
import { ReportResultTable } from "@/components/reports/report-result-table";
import {
  CatalogField,
  ExportFormat,
  ReportDefinition,
  ReportResult,
  exportReport,
  getReport,
  getSelectableObjects,
  runReport,
} from "@/lib/api/reports";

const PAGE_SIZE = 50;

const FORMATS: { key: ExportFormat; label: string; icon: typeof FileText }[] = [
  { key: "excel", label: "Excel", icon: FileSpreadsheet },
  { key: "csv", label: "CSV", icon: FileText },
  { key: "pdf", label: "PDF", icon: FileType },
];

const SCOPE_LABEL: Record<ReportDefinition["scope"], string> = {
  Personal: "شخصي",
  Department: "إدارة",
  Company: "شركة",
  Shared: "مشترك",
};

export default function ReportViewerPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return (
    <AccessGuard anyOf={["Platform.Reports.View"]}>
      <Viewer id={id} />
    </AccessGuard>
  );
}

function Viewer({ id }: { id: string }) {
  const { has } = usePermissions();
  const canExport = has("Platform.Reports.Export");
  const canEdit = has("Platform.Reports.Edit");

  const [definition, setDefinition] = useState<ReportDefinition | null>(null);
  const [fieldsByCode, setFieldsByCode] = useState<Map<string, CatalogField>>(new Map());
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState(false);

  const [draft, setDraft] = useState<ParameterDraft>({});
  const [result, setResult] = useState<ReportResult | null>(null);
  const [running, setRunning] = useState(false);
  const [runError, setRunError] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [exporting, setExporting] = useState<ExportFormat | null>(null);

  /**
   * The parameters the currently displayed `result` was produced with — NOT the live draft.
   * Exports and page changes must reuse these, otherwise a half-edited filter box would silently
   * export a different dataset than the table on screen.
   */
  const appliedRef = useRef<ParameterDraft>({});

  const load = useCallback(async () => {
    setLoading(true);
    setLoadError(false);
    try {
      const def = await getReport(id);
      setDefinition(def);

      // Seed the inputs from each parameter filter's stored default so the user sees what will
      // run. A `Between` filter carries its upper bound in `valueTo`.
      const seed: ParameterDraft = {};
      for (const f of def.filters) {
        if (!f.isParameter) continue;
        if (f.value) seed[toKey(f.fieldCode)] = f.value;
        if (f.operator === "Between" && f.valueTo) seed[toUpperKey(f.fieldCode)] = f.valueTo;
      }
      setDraft(seed);

      // Field metadata (enum options, Arabic labels) lives on the live catalog, keyed by Code,
      // while the definition references an ObjectDefinition Guid — getSelectableObjects joins the
      // two. A catalog failure only downgrades inputs to plain text, so it must not block the page.
      const [catalog] = await Promise.allSettled([getSelectableObjects()]);
      if (catalog.status === "fulfilled") {
        const primary = catalog.value.find((o) => o.id === def.primaryObjectId);
        if (primary) setFieldsByCode(new Map(primary.catalog.fields.map((f) => [f.code, f])));
      }
    } catch {
      setLoadError(true);
    } finally {
      setLoading(false);
    }
  }, [id]);

  // Deferred to a microtask so the effect body doesn't call setState synchronously
  // (matches the codebase's usePermissions pattern).
  useEffect(() => {
    queueMicrotask(() => {
      load();
    });
  }, [load]);

  /**
   * Runs the report. Deliberately NOT called on mount: a parameterized report should execute only
   * after the user has set their filters and pressed "تشغيل التقرير".
   */
  const run = useCallback(
    async (targetPage: number, parameters: ParameterDraft) => {
      setRunning(true);
      setRunError(null);
      try {
        const res = await runReport(id, {
          page: targetPage,
          pageSize: PAGE_SIZE,
          parameters: draftToParameters(parameters),
        });
        setResult(res);
        setPage(targetPage);
        appliedRef.current = parameters;
      } catch (err) {
        setResult(null);
        setRunError(err instanceof Error ? err.message : "تعذر تشغيل التقرير");
        toast.error("تعذر تشغيل التقرير");
      } finally {
        setRunning(false);
      }
    },
    [id],
  );

  const doExport = async (format: ExportFormat) => {
    if (!definition) return;
    setExporting(format);
    try {
      // Pass the parameters the visible table was produced with — omitting them exports the
      // stored defaults, which then disagree with what the user is looking at.
      await exportReport(id, format, definition.code || "report", draftToParameters(appliedRef.current));
      toast.success("تم بدء تنزيل التقرير");
    } catch {
      /* toast already surfaced by exportReport */
    } finally {
      setExporting(null);
    }
  };

  const hasParameters = useMemo(
    () => (definition?.filters ?? []).some((f) => f.isParameter),
    [definition],
  );

  if (loading) {
    return (
      <div className="flex flex-col items-center justify-center border border-border bg-card p-12 text-center" dir="rtl">
        <Loader2 className="mb-3 h-8 w-8 animate-spin text-muted-foreground" />
        <p className="text-sm text-muted-foreground">جارٍ تحميل التقرير…</p>
      </div>
    );
  }

  if (loadError || !definition) {
    return (
      <div className="flex flex-col items-center justify-center border border-border bg-card p-12 text-center" dir="rtl">
        <BarChart3 className="mb-3 h-10 w-10 text-muted-foreground" />
        <p className="mb-2 text-sm text-muted-foreground">تعذر تحميل التقرير</p>
        <button onClick={load} className="text-sm underline hover:no-underline">
          إعادة المحاولة
        </button>
      </div>
    );
  }

  return (
    <div className="space-y-6" dir="rtl">
      <Link
        href="/reports"
        className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowRight className="h-4 w-4" />
        التقارير
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold">{definition.nameAr || definition.nameEn}</h1>
          <div className="mt-1 flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <span>{definition.nameEn}</span>
            {definition.code && <span className="font-mono text-xs">· {definition.code}</span>}
            <span className="border border-border bg-secondary px-2 py-0.5 text-xs">
              {SCOPE_LABEL[definition.scope] ?? definition.scope}
            </span>
            {definition.isPublished ? (
              <span className="border border-green-600/30 bg-green-600/10 px-2 py-0.5 text-xs text-green-700 dark:text-green-400">
                منشور
              </span>
            ) : (
              <span className="border border-border bg-secondary px-2 py-0.5 text-xs">مسودة</span>
            )}
          </div>
          {definition.description && (
            <p className="mt-2 max-w-2xl text-sm text-muted-foreground">{definition.description}</p>
          )}
        </div>

        <div className="flex flex-wrap items-center gap-1.5">
          {canEdit && (
            <Link
              href={`/reports/${id}/edit`}
              className="inline-flex h-9 items-center gap-1.5 border border-border bg-secondary px-3 text-sm hover:bg-secondary/70"
            >
              <Pencil className="h-3.5 w-3.5" />
              تعديل
            </Link>
          )}
          {canExport &&
            FORMATS.map((f) => {
              const Icon = f.icon;
              return (
                <button
                  key={f.key}
                  type="button"
                  onClick={() => doExport(f.key)}
                  disabled={exporting !== null || running}
                  title={`تصدير ${f.label}`}
                  className="inline-flex h-9 items-center gap-1.5 border border-border bg-secondary px-3 text-sm hover:bg-secondary/70 disabled:opacity-50"
                >
                  {exporting === f.key ? (
                    <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  ) : (
                    <Icon className="h-3.5 w-3.5" />
                  )}
                  {f.label}
                </button>
              );
            })}
        </div>
      </div>

      <ReportParameterBar
        filters={definition.filters}
        fieldsByCode={fieldsByCode}
        draft={draft}
        onChange={setDraft}
        onRun={() => run(1, draft)}
        onReset={() => setDraft({})}
        running={running}
        hasRun={result !== null}
      />

      {canExport && result !== null && (
        <p className="text-xs text-muted-foreground">
          يستخدم التصدير نفس المعايير التي شُغّل بها التقرير المعروض.
        </p>
      )}

      {running && result === null ? (
        <div className="flex flex-col items-center justify-center border border-border bg-card p-12 text-center">
          <Loader2 className="mb-3 h-8 w-8 animate-spin text-muted-foreground" />
          <p className="text-sm text-muted-foreground">جارٍ تشغيل التقرير…</p>
        </div>
      ) : runError ? (
        <div className="flex flex-col items-center justify-center border border-border bg-card p-12 text-center">
          <BarChart3 className="mb-3 h-10 w-10 text-muted-foreground" />
          <p className="mb-2 text-sm text-muted-foreground">{runError}</p>
          <button onClick={() => run(1, draft)} className="text-sm underline hover:no-underline">
            إعادة المحاولة
          </button>
        </div>
      ) : result ? (
        <div className={running ? "opacity-60 transition-opacity" : undefined}>
          <ReportResultTable
            result={result}
            page={page}
            onPageChange={(p) => run(p, appliedRef.current)}
            loading={running}
          />
        </div>
      ) : (
        <div className="flex flex-col items-center justify-center border border-border bg-card p-12 text-center">
          <BarChart3 className="mb-4 h-12 w-12 text-muted-foreground" />
          <h2 className="mb-2 text-lg font-semibold">التقرير جاهز للتشغيل</h2>
          <p className="max-w-md text-sm text-muted-foreground">
            {hasParameters
              ? "حدّد المعايير أعلاه ثم اضغط «تشغيل التقرير» لعرض النتائج."
              : "اضغط «تشغيل التقرير» لعرض النتائج."}
          </p>
        </div>
      )}
    </div>
  );
}
