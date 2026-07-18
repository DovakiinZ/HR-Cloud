"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { Loader2, Pencil, Plus, RefreshCw, Sparkles, Check, LayoutTemplate, Share2, Printer } from "lucide-react";
import { toast } from "sonner";
import { usePermissions } from "@/lib/permissions";
import {
  addWidget, deleteWidget, getDashboard, listDashboards, saveLayout,
  seedDefaultDashboard, widgetPayloadFromSpec,
} from "@/lib/api/dashboards";
import { DashboardDefinition, DashboardWidget, WidgetFilterSpec, WidgetQuerySpec } from "@/types/dashboard";
import { ApiError } from "@/lib/api-client";
import { DashboardGrid } from "@/components/dashboard/dashboard-grid";
import { DashboardFilterBar } from "@/components/dashboard/filter-bar";
import { WidgetBuilder } from "@/components/dashboard/widget-builder";
import { BusinessWidgetBuilder } from "@/components/dashboard/business-widget-builder";
import { DashboardShareDialog } from "@/components/dashboard/share-dialog";

const REFRESH_OPTIONS = [
  { label: "تحديث يدوي", value: 0 },
  { label: "كل دقيقة", value: 60_000 },
  { label: "كل ٥ دقائق", value: 300_000 },
  { label: "كل ١٥ دقيقة", value: 900_000 },
  { label: "كل ساعة", value: 3_600_000 },
];

export default function DashboardPage() {
  const { has } = usePermissions();
  const canEdit = has("Platform.Dashboards.Edit");
  const canCreate = has("Platform.Dashboards.Create");
  const canDelete = has("Platform.Dashboards.Delete");

  const [dashboards, setDashboards] = useState<DashboardDefinition[]>([]);
  const [activeId, setActiveId] = useState<string>("");
  const [dashboard, setDashboard] = useState<DashboardDefinition | null>(null);
  const [filters, setFilters] = useState<WidgetFilterSpec[]>([]);
  const [editMode, setEditMode] = useState(false);
  const [builderOpen, setBuilderOpen] = useState(false);
  const [advanced, setAdvanced] = useState(false);
  const [shareOpen, setShareOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [loading, setLoading] = useState(true);
  const [seeding, setSeeding] = useState(false);
  const [refreshMs, setRefreshMs] = useState(0);
  const [refreshKey, setRefreshKey] = useState(0);

  const loadList = useCallback(async (preferId?: string) => {
    setLoading(true);
    try {
      const list = await listDashboards();
      setDashboards(list);
      const pick = preferId ?? list.find((d) => d.isDefault)?.id ?? list[0]?.id ?? "";
      setActiveId(pick);
    } catch (e) {
      if (!(e instanceof ApiError) || ![401, 403, 500].includes(e.status))
        toast.error("تعذر تحميل اللوحات");
    } finally {
      setLoading(false);
    }
  }, []);

  const loadDetail = useCallback(async (id: string) => {
    if (!id) { setDashboard(null); return; }
    try {
      setDashboard(await getDashboard(id));
    } catch {
      setDashboard(null);
    }
  }, []);

  useEffect(() => { loadList(); }, [loadList]);
  useEffect(() => { loadDetail(activeId); }, [activeId, loadDetail]);

  // Real-time auto-refresh: bump the key so every widget re-fetches on interval.
  useEffect(() => {
    if (!refreshMs) return;
    const t = setInterval(() => setRefreshKey((k) => k + 1), refreshMs);
    return () => clearInterval(t);
  }, [refreshMs]);

  const onSeed = async () => {
    setSeeding(true);
    try {
      const id = await seedDefaultDashboard();
      toast.success("تم إنشاء اللوحة التنفيذية");
      await loadList(typeof id === "string" ? id : undefined);
    } catch {
      toast.error("تعذر إنشاء اللوحة الافتراضية");
    } finally {
      setSeeding(false);
    }
  };

  const onSaveWidget = async (args: { spec: WidgetQuerySpec; visualization: string; titleAr: string; titleEn: string }) => {
    if (!activeId) return;
    setSaving(true);
    try {
      const payload = widgetPayloadFromSpec(args.visualization, args.titleAr, args.titleEn, args.spec);
      await addWidget(activeId, payload);
      toast.success("تمت إضافة العنصر");
      setBuilderOpen(false);
      await loadDetail(activeId);
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : "تعذر حفظ العنصر");
    } finally {
      setSaving(false);
    }
  };

  const onDeleteWidget = async (w: DashboardWidget) => {
    if (!confirm(`حذف العنصر "${w.titleAr}"؟`)) return;
    try {
      await deleteWidget(w.id);
      setDashboard((d) => d ? { ...d, widgets: d.widgets.filter((x) => x.id !== w.id) } : d);
    } catch {
      toast.error("تعذر حذف العنصر");
    }
  };

  const onPersistLayout = async (layouts: { widgetId: string; column: number; row: number; width: number; height: number }[]) => {
    if (!activeId) return;
    try { await saveLayout(activeId, layouts); }
    catch { /* layout already applied client-side; surface quietly */ }
  };

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold">لوحات المعلومات</h1>
          <p className="mt-1 text-sm text-muted-foreground">مؤشرات حية تُبنى من سجل الكائنات</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {dashboard && (
            <select value={refreshMs} onChange={(e) => setRefreshMs(Number(e.target.value))} className="h-10 border border-border bg-secondary px-2 text-sm" title="التحديث التلقائي">
              {REFRESH_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
            </select>
          )}
          <button onClick={() => { setRefreshKey((k) => k + 1); loadDetail(activeId); }} className="inline-flex h-10 items-center gap-2 border border-border px-3 text-sm hover:bg-muted" title="تحديث">
            <RefreshCw className="h-4 w-4" />
          </button>
          {dashboard && (
            <button onClick={() => window.print()} className="inline-flex h-10 items-center gap-2 border border-border px-3 text-sm hover:bg-muted" title="طباعة / PDF">
              <Printer className="h-4 w-4" />
            </button>
          )}
          {canEdit && dashboard && (
            <button onClick={() => setShareOpen(true)} className="inline-flex h-10 items-center gap-2 border border-border px-4 text-sm hover:bg-muted">
              <Share2 className="h-4 w-4" /> مشاركة
            </button>
          )}
          <Link href="/dashboard/templates" className="inline-flex h-10 items-center gap-2 border border-border px-4 text-sm hover:bg-muted">
            <LayoutTemplate className="h-4 w-4" /> القوالب
          </Link>
          {canCreate && (
            <Link href="/dashboard/builder" className="inline-flex h-10 items-center gap-2 border border-border px-4 text-sm hover:bg-muted">
              <Sparkles className="h-4 w-4" /> منشئ العناصر
            </Link>
          )}
          {canEdit && dashboard && (
            <button onClick={() => setEditMode((v) => !v)}
              className={`inline-flex h-10 items-center gap-2 px-4 text-sm font-bold uppercase tracking-wider ${editMode ? "bg-primary text-primary-foreground" : "border border-border hover:bg-muted"}`}>
              {editMode ? <><Check className="h-4 w-4" /> تم</> : <><Pencil className="h-4 w-4" /> تحرير</>}
            </button>
          )}
        </div>
      </div>

      {/* Dashboard tabs */}
      {dashboards.length > 0 && (
        <div className="flex flex-wrap items-center gap-1 border-b border-border">
          {dashboards.map((d) => (
            <button key={d.id} onClick={() => { setActiveId(d.id); setEditMode(false); }}
              className={`-mb-px border-b-2 px-4 py-2 text-sm ${activeId === d.id ? "border-primary font-bold text-foreground" : "border-transparent text-muted-foreground hover:text-foreground"}`}>
              {d.nameAr}
            </button>
          ))}
        </div>
      )}

      {/* Filter bar */}
      {dashboard && <DashboardFilterBar onChange={setFilters} />}

      {/* Edit toolbar */}
      {editMode && dashboard && (
        <div className="flex items-center gap-2 border border-primary/40 bg-primary/5 px-3 py-2 text-sm">
          <span className="text-muted-foreground">وضع التحرير — اسحب لإعادة الترتيب، اسحب الحافة لتغيير الحجم</span>
          <button onClick={() => setBuilderOpen(true)} className="ms-auto inline-flex h-8 items-center gap-1 bg-primary px-3 text-xs font-bold uppercase tracking-wider text-primary-foreground hover:bg-primary/80">
            <Plus className="h-3.5 w-3.5" /> إضافة عنصر
          </button>
        </div>
      )}

      {/* Body */}
      {loading ? (
        <div className="flex h-64 items-center justify-center text-muted-foreground"><Loader2 className="h-6 w-6 animate-spin" /></div>
      ) : dashboards.length === 0 ? (
        <div className="flex flex-col items-center justify-center gap-4 border border-dashed border-border py-20 text-center">
          <p className="text-muted-foreground">لا توجد لوحات بعد</p>
          {canCreate ? (
            <div className="flex gap-2">
              <button onClick={onSeed} disabled={seeding} className="inline-flex h-10 items-center gap-2 bg-primary px-5 text-sm font-bold uppercase tracking-wider text-primary-foreground hover:bg-primary/80 disabled:opacity-50">
                {seeding ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />} إنشاء اللوحة التنفيذية
              </button>
              <Link href="/dashboard/builder" className="inline-flex h-10 items-center gap-2 border border-border px-5 text-sm hover:bg-muted">
                <Plus className="h-4 w-4" /> بناء لوحة مخصصة
              </Link>
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">لا تملك صلاحية إنشاء اللوحات</p>
          )}
        </div>
      ) : dashboard ? (
        <DashboardGrid
          widgets={dashboard.widgets}
          filters={filters}
          editMode={editMode}
          refreshKey={refreshKey}
          onDeleteWidget={canDelete ? onDeleteWidget : undefined}
          onPersistLayout={onPersistLayout}
        />
      ) : (
        <div className="flex h-40 items-center justify-center text-muted-foreground"><Loader2 className="h-5 w-5 animate-spin" /></div>
      )}

      {/* Share dialog */}
      {shareOpen && dashboard && (
        <DashboardShareDialog
          dashboardId={dashboard.id}
          dashboardName={dashboard.nameAr}
          shares={dashboard.shares ?? []}
          onClose={() => setShareOpen(false)}
          onChanged={() => loadDetail(activeId)}
        />
      )}

      {/* Builder modal */}
      {builderOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="absolute inset-0 bg-black/60" onClick={() => { if (!saving) { setBuilderOpen(false); setAdvanced(false); } }} />
          <div className="relative z-10 w-full max-w-4xl border border-border bg-card shadow-xl">
            {advanced ? (
              <WidgetBuilder onSave={onSaveWidget} onCancel={() => setAdvanced(false)} saving={saving} />
            ) : (
              <BusinessWidgetBuilder
                dashboardId={activeId}
                onSaved={async () => { setBuilderOpen(false); setAdvanced(false); await loadDetail(activeId); }}
                onCancel={() => { setBuilderOpen(false); setAdvanced(false); }}
                onAdvanced={() => setAdvanced(true)}
              />
            )}
          </div>
        </div>
      )}
    </div>
  );
}
