"use client";

import { useCallback, useEffect, useState } from "react";
import { BarChart3, Download, FileSpreadsheet, FileText, FileType, Loader2, RefreshCw } from "lucide-react";
import { toast } from "sonner";
import { usePermission } from "@/lib/permissions";
import { getReports, exportReport, ReportDefinition, ExportFormat } from "@/lib/api/reports";

const FORMATS: { key: ExportFormat; label: string; icon: typeof FileText }[] = [
  { key: "excel", label: "Excel", icon: FileSpreadsheet },
  { key: "csv", label: "CSV", icon: FileText },
  { key: "pdf", label: "PDF", icon: FileType },
];

export default function ReportsPage() {
  const { allowed: canExport } = usePermission("Platform.Reports.Export");
  const [reports, setReports] = useState<ReportDefinition[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  // reportId currently exporting → which format, to show a per-button spinner.
  const [busy, setBusy] = useState<string | null>(null);

  const fetchReports = useCallback(async () => {
    setLoading(true);
    setError(false);
    try {
      const res = await getReports({ pageSize: 100 });
      setReports(res.items ?? []);
    } catch {
      setError(true);
    } finally {
      setLoading(false);
    }
  }, []);

  // Defer to a microtask so the effect body doesn't call setState synchronously
  // (matches the codebase's usePermissions pattern).
  useEffect(() => {
    queueMicrotask(() => { fetchReports(); });
  }, [fetchReports]);

  const doExport = async (report: ReportDefinition, format: ExportFormat) => {
    const key = `${report.id}:${format}`;
    setBusy(key);
    try {
      await exportReport(report.id, format, report.code || "report");
      toast.success("تم بدء تنزيل التقرير");
    } catch {
      /* toast already surfaced by exportReport */
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="space-y-6" dir="rtl">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold">التقارير</h1>
          <p className="text-sm text-muted-foreground mt-1">التقارير والإحصائيات — تشغيل وتصدير</p>
        </div>
        <button
          onClick={fetchReports}
          className="inline-flex h-9 items-center gap-2 border border-border bg-secondary px-3 text-sm hover:bg-secondary/70"
        >
          <RefreshCw className="h-4 w-4" />
          تحديث
        </button>
      </div>

      {loading ? (
        <div className="border border-border bg-card p-12 flex flex-col items-center justify-center text-center">
          <Loader2 className="h-8 w-8 animate-spin text-muted-foreground mb-3" />
          <p className="text-sm text-muted-foreground">جارٍ تحميل التقارير…</p>
        </div>
      ) : error ? (
        <div className="border border-border bg-card p-12 flex flex-col items-center justify-center text-center">
          <BarChart3 className="h-10 w-10 text-muted-foreground mb-3" />
          <p className="text-sm text-muted-foreground mb-2">تعذر تحميل التقارير</p>
          <button onClick={fetchReports} className="text-sm underline hover:no-underline">
            إعادة المحاولة
          </button>
        </div>
      ) : reports.length === 0 ? (
        <div className="border border-border bg-card p-12 flex flex-col items-center justify-center text-center">
          <BarChart3 className="h-12 w-12 text-muted-foreground mb-4" />
          <h2 className="text-lg font-semibold mb-2">لا توجد تقارير</h2>
          <p className="text-sm text-muted-foreground">لم يتم إنشاء أي تقارير بعد.</p>
        </div>
      ) : (
        <div className="border border-border bg-card overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-border text-right text-xs uppercase tracking-wider text-muted-foreground">
                <th className="px-4 py-3 font-medium">التقرير</th>
                <th className="px-4 py-3 font-medium">النوع</th>
                <th className="px-4 py-3 font-medium">الحالة</th>
                <th className="px-4 py-3 font-medium">تصدير</th>
              </tr>
            </thead>
            <tbody>
              {reports.map((r) => (
                <tr key={r.id} className="border-b border-border/60 last:border-0 hover:bg-secondary/40">
                  <td className="px-4 py-3">
                    <div className="font-medium">{r.nameAr || r.nameEn}</div>
                    <div className="text-xs text-muted-foreground">{r.nameEn}{r.code ? ` · ${r.code}` : ""}</div>
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">{r.reportType}</td>
                  <td className="px-4 py-3">
                    {r.isPublished ? (
                      <span className="inline-block border border-green-600/30 bg-green-600/10 px-2 py-0.5 text-xs text-green-700 dark:text-green-400">
                        منشور
                      </span>
                    ) : (
                      <span className="inline-block border border-border bg-secondary px-2 py-0.5 text-xs text-muted-foreground">
                        مسودة
                      </span>
                    )}
                  </td>
                  <td className="px-4 py-3">
                    {canExport ? (
                      <div className="flex items-center gap-1.5">
                        {FORMATS.map((f) => {
                          const key = `${r.id}:${f.key}`;
                          const Icon = f.icon;
                          return (
                            <button
                              key={f.key}
                              onClick={() => doExport(r, f.key)}
                              disabled={busy !== null}
                              title={`تصدير ${f.label}`}
                              className="inline-flex h-8 items-center gap-1.5 border border-border bg-secondary px-2.5 text-xs hover:bg-secondary/70 disabled:opacity-50"
                            >
                              {busy === key ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Icon className="h-3.5 w-3.5" />}
                              {f.label}
                            </button>
                          );
                        })}
                      </div>
                    ) : (
                      <span className="inline-flex items-center gap-1.5 text-xs text-muted-foreground">
                        <Download className="h-3.5 w-3.5" />
                        غير مصرح
                      </span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
