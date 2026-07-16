"use client";

import { use, useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { AlertTriangle, Loader2, Pencil, RefreshCw } from "lucide-react";
import { toast } from "sonner";
import { usePermission } from "@/lib/permissions";
import { getReport, runReport, exportReport, ReportDetail, ReportResult, ExportFormat } from "@/lib/api/reports";
import { ReportTable } from "@/components/reports/report-table";
import { SchedulePanel } from "@/components/reports/schedule-panel";

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

      {canEdit && <SchedulePanel reportId={id} />}
    </div>
  );
}
