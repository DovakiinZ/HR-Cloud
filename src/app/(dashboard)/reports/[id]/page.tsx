"use client";

import { use, useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { AlertTriangle, ArrowRight, Loader2, Pencil, RefreshCw } from "lucide-react";
import { toast } from "sonner";
import { AccessGuard } from "@/components/access/access-guard";
import { usePermission } from "@/lib/permissions";
import { getReport, runReport, exportReport, ReportDefinition, ReportResult, ExportFormat } from "@/lib/api/reports";
import { ReportTable } from "@/components/reports/report-table";
import { SchedulePanel } from "@/components/reports/schedule-panel";
import { ReportParameters } from "@/components/reports/report-parameters";

const FORMATS: { key: ExportFormat; label: string }[] = [
  { key: "excel", label: "Excel" }, { key: "csv", label: "CSV" }, { key: "pdf", label: "PDF" },
  { key: "sif", label: "WPS/SIF" },
];

const SCOPE_LABEL: Record<string, string> = {
  Personal: "شخصي", Department: "إدارة", Company: "شركة", Shared: "مشترك",
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
  // Deferred to a microtask so the effect body does not call setState synchronously, matching
  // the load() effect above and the codebase's usePermissions pattern.
  useEffect(() => {
    if (paramFilters.length === 0) return;
    queueMicrotask(() => {
      setDraft((prev) => {
        if (Object.keys(prev).length > 0) return prev;
        const seed: Record<string, string> = {};
        for (const f of paramFilters) {
          if (f.value != null) seed[f.fieldCode] = f.value;
          if (f.operator === "Between" && f.valueTo != null) seed[`${f.fieldCode}:to`] = f.valueTo;
        }
        return seed;
      });
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
      <Link href="/reports" className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground">
        <ArrowRight className="h-4 w-4" />
        التقارير
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold">{report?.nameAr || report?.nameEn || "تقرير"}</h1>
          <div className="mt-1 flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <span>{report?.nameEn}{report?.code ? ` · ${report.code}` : ""}</span>
            {report && (
              <span className="border border-border bg-secondary px-2 py-0.5 text-xs">
                {SCOPE_LABEL[report.scope] ?? report.scope}
              </span>
            )}
            {report?.isPublished ? (
              <span className="border border-green-600/30 bg-green-600/10 px-2 py-0.5 text-xs text-green-700 dark:text-green-400">منشور</span>
            ) : report ? (
              <span className="border border-border bg-secondary px-2 py-0.5 text-xs">مسودة</span>
            ) : null}
          </div>
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
      ) : result && !result.groups?.length && !result.rows?.length ? (
        <div className="border border-border bg-card p-12 flex flex-col items-center text-center">
          <p className="text-sm font-medium mb-1">لا توجد نتائج مطابقة</p>
          <p className="text-xs text-muted-foreground">جرّب تعديل معاملات التشغيل أو عوامل التصفية.</p>
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
