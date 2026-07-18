"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowRight, Loader2, Plus, Sparkles } from "lucide-react";
import { toast } from "sonner";
import { usePermissions } from "@/lib/permissions";
import {
  addWidget, createDashboard, listDashboards, seedDefaultDashboard, widgetPayloadFromSpec,
} from "@/lib/api/dashboards";
import { DASHBOARD_SCOPE, DashboardDefinition, WidgetQuerySpec } from "@/types/dashboard";
import { ApiError } from "@/lib/api-client";
import { WidgetBuilder } from "@/components/dashboard/widget-builder";
import { BusinessWidgetBuilder } from "@/components/dashboard/business-widget-builder";

export default function BuilderPage() {
  const router = useRouter();
  const { has } = usePermissions();
  const canCreate = has("Platform.Dashboards.Create");

  const [dashboards, setDashboards] = useState<DashboardDefinition[]>([]);
  const [targetId, setTargetId] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [creating, setCreating] = useState(false);
  const [advanced, setAdvanced] = useState(false);

  const load = useCallback(async (preferId?: string) => {
    setLoading(true);
    try {
      const list = await listDashboards();
      setDashboards(list);
      setTargetId(preferId ?? list.find((d) => d.isDefault)?.id ?? list[0]?.id ?? "");
    } catch {
      /* handled globally */
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const onCreateDashboard = async () => {
    const name = prompt("اسم اللوحة الجديدة");
    if (!name?.trim()) return;
    setCreating(true);
    try {
      const d = await createDashboard({
        code: `dash-${Date.now().toString(36)}`,
        nameAr: name.trim(),
        nameEn: name.trim(),
        scope: DASHBOARD_SCOPE.Personal,
      });
      toast.success("تم إنشاء اللوحة");
      await load(d.id);
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : "تعذر إنشاء اللوحة");
    } finally {
      setCreating(false);
    }
  };

  const onSeed = async () => {
    setCreating(true);
    try {
      const id = await seedDefaultDashboard();
      toast.success("تم إنشاء اللوحة التنفيذية");
      await load(typeof id === "string" ? id : undefined);
    } catch {
      toast.error("تعذر إنشاء اللوحة الافتراضية");
    } finally {
      setCreating(false);
    }
  };

  const onSave = async (args: { spec: WidgetQuerySpec; visualization: string; titleAr: string; titleEn: string }) => {
    if (!targetId) { toast.error("اختر لوحة لإضافة العنصر إليها"); return; }
    setSaving(true);
    try {
      await addWidget(targetId, widgetPayloadFromSpec(args.visualization, args.titleAr, args.titleEn, args.spec));
      toast.success("تمت إضافة العنصر إلى اللوحة");
      router.push("/dashboard");
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : "تعذر حفظ العنصر");
    } finally {
      setSaving(false);
    }
  };

  if (!canCreate) {
    return <div className="border border-border bg-card p-10 text-center text-muted-foreground">لا تملك صلاحية إنشاء العناصر</div>;
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold">منشئ العناصر</h1>
          <p className="mt-1 text-sm text-muted-foreground">أنشئ مؤشراً أو رسماً بيانياً من أي كائن في النظام</p>
        </div>
        <Link href="/dashboard" className="inline-flex h-10 items-center gap-2 border border-border px-4 text-sm hover:bg-muted">
          <ArrowRight className="h-4 w-4" /> العودة للوحات
        </Link>
      </div>

      {loading ? (
        <div className="flex h-64 items-center justify-center text-muted-foreground"><Loader2 className="h-6 w-6 animate-spin" /></div>
      ) : dashboards.length === 0 ? (
        <div className="flex flex-col items-center justify-center gap-4 border border-dashed border-border py-20 text-center">
          <p className="text-muted-foreground">أنشئ لوحة أولاً لإضافة العناصر إليها</p>
          <div className="flex gap-2">
            <button onClick={onSeed} disabled={creating} className="inline-flex h-10 items-center gap-2 bg-primary px-5 text-sm font-bold uppercase tracking-wider text-primary-foreground hover:bg-primary/80 disabled:opacity-50">
              {creating ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />} لوحة تنفيذية جاهزة
            </button>
            <button onClick={onCreateDashboard} disabled={creating} className="inline-flex h-10 items-center gap-2 border border-border px-5 text-sm hover:bg-muted disabled:opacity-50">
              <Plus className="h-4 w-4" /> لوحة فارغة
            </button>
          </div>
        </div>
      ) : (
        <>
          <div className="flex flex-wrap items-center gap-2 border border-border bg-card px-3 py-2">
            <label className="text-xs font-bold uppercase tracking-wider text-muted-foreground">أضف إلى لوحة</label>
            <select value={targetId} onChange={(e) => setTargetId(e.target.value)} className="h-9 border border-border bg-secondary px-3 text-sm">
              {dashboards.map((d) => <option key={d.id} value={d.id}>{d.nameAr}</option>)}
            </select>
            <button onClick={onCreateDashboard} disabled={creating} className="inline-flex h-9 items-center gap-1 border border-border px-3 text-sm hover:bg-muted disabled:opacity-50">
              <Plus className="h-3.5 w-3.5" /> لوحة جديدة
            </button>
          </div>

          <div className="border border-border bg-card">
            {advanced ? (
              <WidgetBuilder onSave={onSave} onCancel={() => setAdvanced(false)} saving={saving} />
            ) : (
              <BusinessWidgetBuilder
                dashboardId={targetId}
                onSaved={() => router.push("/dashboard")}
                onCancel={() => router.push("/dashboard")}
                onAdvanced={() => setAdvanced(true)}
              />
            )}
          </div>
        </>
      )}
    </div>
  );
}
