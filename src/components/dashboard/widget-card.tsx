"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { Download, GripVertical, Image as ImageIcon, Loader2, Pencil, RefreshCw, Trash2, AlertCircle, FileSpreadsheet } from "lucide-react";
import { executeWidget, exportWidget, previewWidgetData } from "@/lib/api/dashboards";
import { SeriesPoint, WidgetDataResult, WidgetFilterSpec, WidgetQuerySpec } from "@/types/dashboard";
import { exportCsv, exportPng } from "@/lib/dashboard-export";
import { ApiError } from "@/lib/api-client";
import { WidgetRenderer } from "./widget-renderer";
import { DrilldownDrawer } from "./drilldown-drawer";

interface WidgetCardProps {
  widgetId?: string;
  spec: WidgetQuerySpec;
  visualization: string;
  titleAr: string;
  dashboardFilters?: WidgetFilterSpec[];
  editMode?: boolean;
  refreshKey?: number;
  onEdit?: () => void;
  onDelete?: () => void;
  dragHandleClass?: string;
  enableDrilldown?: boolean;
}

export function WidgetCard({
  widgetId, spec, visualization, titleAr, dashboardFilters,
  editMode, refreshKey, onEdit, onDelete, dragHandleClass, enableDrilldown = true,
}: WidgetCardProps) {
  const [data, setData] = useState<WidgetDataResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);
  const [drill, setDrill] = useState<{ key: string | null; label: string } | null>(null);
  const [menu, setMenu] = useState(false);
  const bodyRef = useRef<HTMLDivElement>(null);

  const filterKey = JSON.stringify(dashboardFilters ?? []);
  const specKey = JSON.stringify(spec);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    setForbidden(false);
    try {
      const res = widgetId
        ? await executeWidget(widgetId, dashboardFilters)
        : await previewWidgetData(spec, dashboardFilters);
      setData(res);
    } catch (e) {
      if (e instanceof ApiError && e.status === 403) setForbidden(true);
      else setError(e instanceof Error ? e.message : "تعذر تحميل البيانات");
    } finally {
      setLoading(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [widgetId, specKey, filterKey, refreshKey]);

  useEffect(() => { load(); }, [load]);

  const onSelect = (p: SeriesPoint) => {
    if (enableDrilldown && !editMode) setDrill({ key: p.key, label: p.label });
  };

  if (forbidden) {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-1 border border-border bg-card text-center text-xs text-muted-foreground">
        <AlertCircle className="h-4 w-4" />
        <span>محجوب — لا تملك صلاحية</span>
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col border border-border bg-card">
      {/* Header */}
      <div className="flex items-center justify-between gap-2 border-b border-border px-3 py-2">
        <div className="flex min-w-0 items-center gap-1.5">
          {editMode && (
            <span className={`${dragHandleClass} cursor-move text-muted-foreground`}>
              <GripVertical className="h-4 w-4" />
            </span>
          )}
          <h3 className="truncate text-sm font-medium" title={titleAr}>{titleAr}</h3>
        </div>
        <div className="flex items-center gap-1">
          {editMode ? (
            <>
              {onEdit && <button onClick={onEdit} className="text-muted-foreground hover:text-foreground" title="تعديل"><Pencil className="h-3.5 w-3.5" /></button>}
              {onDelete && <button onClick={onDelete} className="text-muted-foreground hover:text-destructive" title="حذف"><Trash2 className="h-3.5 w-3.5" /></button>}
            </>
          ) : (
            <>
              <div className="relative">
                <button onClick={() => setMenu((v) => !v)} className="text-muted-foreground hover:text-foreground" title="تصدير"><Download className="h-3.5 w-3.5" /></button>
                {menu && (
                  <>
                    <div className="fixed inset-0 z-10" onClick={() => setMenu(false)} />
                    <div className="absolute left-0 z-20 mt-1 w-36 border border-border bg-card py-1 text-sm shadow-lg">
                      <button onClick={() => { setMenu(false); if (data) exportCsv(titleAr, data); }} className="flex w-full items-center gap-2 px-3 py-1.5 hover:bg-muted">
                        <FileSpreadsheet className="h-3.5 w-3.5" /> CSV
                      </button>
                      <button onClick={() => { setMenu(false); if (bodyRef.current) exportPng(titleAr, bodyRef.current); }} className="flex w-full items-center gap-2 px-3 py-1.5 hover:bg-muted">
                        <ImageIcon className="h-3.5 w-3.5" /> PNG
                      </button>
                      {widgetId && (
                        <>
                          <button onClick={() => { setMenu(false); exportWidget(widgetId, "excel", titleAr); }} className="flex w-full items-center gap-2 px-3 py-1.5 hover:bg-muted">
                            <FileSpreadsheet className="h-3.5 w-3.5" /> Excel
                          </button>
                          <button onClick={() => { setMenu(false); exportWidget(widgetId, "pdf", titleAr); }} className="flex w-full items-center gap-2 px-3 py-1.5 hover:bg-muted">
                            <Download className="h-3.5 w-3.5" /> PDF
                          </button>
                        </>
                      )}
                    </div>
                  </>
                )}
              </div>
              <button onClick={load} className="text-muted-foreground hover:text-foreground" title="تحديث">
                <RefreshCw className={`h-3.5 w-3.5 ${loading ? "animate-spin" : ""}`} />
              </button>
            </>
          )}
        </div>
      </div>

      {/* Body */}
      <div ref={bodyRef} className="relative min-h-0 flex-1 p-3">
        {loading && <div className="absolute inset-0 flex items-center justify-center text-muted-foreground"><Loader2 className="h-5 w-5 animate-spin" /></div>}
        {!loading && error && (
          <div className="flex h-full flex-col items-center justify-center gap-2 text-center text-sm text-destructive">
            <AlertCircle className="h-5 w-5" /><span>{error}</span>
            <button onClick={load} className="text-xs underline">إعادة المحاولة</button>
          </div>
        )}
        {!loading && !error && data && <WidgetRenderer type={visualization} result={data} onSelect={onSelect} />}
      </div>

      {drill && (
        <DrilldownDrawer open title={titleAr} spec={spec} segmentKey={drill.key} segmentLabel={drill.label} dashboardFilters={dashboardFilters} onClose={() => setDrill(null)} />
      )}
    </div>
  );
}
