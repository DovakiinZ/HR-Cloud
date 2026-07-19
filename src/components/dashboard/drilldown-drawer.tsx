"use client";

import { useCallback, useEffect, useState } from "react";
import { X, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { drilldownWidget, exportDrilldown, WidgetExportFormat } from "@/lib/api/dashboards";
import { WidgetDataResult, WidgetFilterSpec, WidgetQuerySpec } from "@/types/dashboard";

interface DrilldownDrawerProps {
  open: boolean;
  title: string;
  spec: WidgetQuerySpec;
  segmentKey: string | null;
  segmentLabel?: string;
  dashboardFilters?: WidgetFilterSpec[];
  onClose: () => void;
}

const PAGE_SIZE = 25;

export function DrilldownDrawer({ open, title, spec, segmentKey, segmentLabel, dashboardFilters, onClose }: DrilldownDrawerProps) {
  const [data, setData] = useState<WidgetDataResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [page, setPage] = useState(1);
  const [exporting, setExporting] = useState(false);

  const handleExport = useCallback(async (format: WidgetExportFormat) => {
    setExporting(true);
    try {
      await exportDrilldown(spec, segmentKey, format, dashboardFilters, title);
    } catch {
      toast.error("تعذر تصدير البيانات");
    } finally {
      setExporting(false);
    }
  }, [spec, segmentKey, dashboardFilters, title]);

  const load = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const res = await drilldownWidget(spec, segmentKey, dashboardFilters, p, PAGE_SIZE);
      setData(res);
      setPage(p);
    } catch {
      setData(null);
    } finally {
      setLoading(false);
    }
  }, [spec, segmentKey, dashboardFilters]);

  useEffect(() => {
    if (open) load(1);
  }, [open, load]);

  if (!open) return null;

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / PAGE_SIZE)) : 1;

  return (
    <div className="fixed inset-0 z-50 flex justify-start">
      <div className="absolute inset-0 bg-black/50" onClick={onClose} />
      <div className="relative z-10 flex h-full w-full max-w-2xl flex-col border-l border-border bg-background shadow-xl">
        <div className="flex items-center justify-between border-b border-border px-5 py-4">
          <div>
            <h3 className="font-bold">{title}</h3>
            {segmentLabel && <p className="mt-0.5 text-xs text-muted-foreground">{segmentLabel}</p>}
          </div>
          <div className="flex items-center gap-2">
            <button
              disabled={exporting || loading}
              onClick={() => handleExport("excel")}
              className="h-7 px-2.5 text-xs border border-border hover:bg-muted disabled:opacity-40"
            >
              تصدير Excel
            </button>
            <button
              disabled={exporting || loading}
              onClick={() => handleExport("pdf")}
              className="h-7 px-2.5 text-xs border border-border hover:bg-muted disabled:opacity-40"
            >
              PDF
            </button>
            <button
              disabled={exporting || loading}
              onClick={() => handleExport("csv")}
              className="h-7 px-2.5 text-xs border border-border hover:bg-muted disabled:opacity-40"
            >
              CSV
            </button>
            <button onClick={onClose} className="mr-1 text-muted-foreground hover:text-foreground"><X className="h-5 w-5" /></button>
          </div>
        </div>

        <div className="flex-1 overflow-auto p-2">
          {loading && (
            <div className="flex h-40 items-center justify-center text-muted-foreground">
              <Loader2 className="h-5 w-5 animate-spin" />
            </div>
          )}
          {!loading && data && data.rows.length > 0 && (
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">
                  {data.columns.map((c) => <th key={c.code} className="px-3 py-2 whitespace-nowrap">{c.label}</th>)}
                </tr>
              </thead>
              <tbody>
                {data.rows.map((row, i) => (
                  <tr key={i} className="border-b border-border/40 hover:bg-muted/40">
                    {data.columns.map((c) => (
                      <td key={c.code} className="px-3 py-2 whitespace-nowrap">
                        {row[c.code] === null || row[c.code] === undefined
                          ? <span className="text-muted-foreground">—</span>
                          : typeof row[c.code] === "boolean" ? (row[c.code] ? "نعم" : "لا")
                          : String(row[c.code])}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          {!loading && data && data.rows.length === 0 && (
            <div className="flex h-40 items-center justify-center text-sm text-muted-foreground">لا توجد سجلات</div>
          )}
        </div>

        {data && data.totalCount > PAGE_SIZE && (
          <div className="flex items-center justify-between border-t border-border px-5 py-3 text-sm">
            <span className="text-muted-foreground">{data.totalCount} سجل</span>
            <div className="flex items-center gap-2">
              <button disabled={page <= 1 || loading} onClick={() => load(page - 1)} className="h-8 px-3 border border-border disabled:opacity-40 hover:bg-muted">السابق</button>
              <span className="text-xs text-muted-foreground">{page} / {totalPages}</span>
              <button disabled={page >= totalPages || loading} onClick={() => load(page + 1)} className="h-8 px-3 border border-border disabled:opacity-40 hover:bg-muted">التالي</button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
